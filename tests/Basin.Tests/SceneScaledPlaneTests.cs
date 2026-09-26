using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class SceneScaledPlaneTests
{
    [Fact]
    public void A_resting_scaled_buffer_on_whole_pixels_is_offered_with_its_scaled_box_and_the_whole_buffer()
    {
        using var rig = new Rig();
        rig.Frame.Matrix = new RenderTransform(0.5, 0, 20, 0, 0.5, 30, 0, 0, 1);
        rig.Commit(4);

        Assert.True(rig.Scene.OffloadCommits > 0, "the scaled buffer reaches a plane");
        Assert.NotEmpty(rig.Offered);
        foreach (var layer in rig.Offered)
        {
            Assert.Equal(new Box(20 + 5, 30 + 5, 20, 20), layer.DstBox);
            Assert.True(layer.SrcBox.IsEmpty, $"the source is the whole buffer, not {layer.SrcBox}");
        }

        Assert.Equal(0, rig.Scene.DeclinedFor(PlaneDeclineReason.Transformed));
    }

    [Fact]
    public void A_scaled_buffer_off_whole_pixels_declines_as_a_scaled_fraction()
    {
        using var rig = new Rig();
        rig.Frame.Matrix = new RenderTransform(0.5, 0, 20.25, 0, 0.5, 30, 0, 0, 1);
        rig.Commit(4);

        Assert.Equal(0, rig.Scene.OffloadCommits);
        Assert.True(rig.Scene.DeclinedFor(PlaneDeclineReason.ScaledFraction) > 0);
    }

    [Fact]
    public void A_rotated_buffer_still_declines_as_transformed()
    {
        using var rig = new Rig();
        rig.Frame.Matrix = RenderTransform.RotationAbout(0.2, 40, 40);
        rig.Commit(4);

        Assert.Equal(0, rig.Scene.OffloadCommits);
        Assert.True(rig.Scene.DeclinedFor(PlaneDeclineReason.Transformed) > 0);
    }

    [Fact]
    public void A_scaled_buffer_that_moves_every_frame_is_settling()
    {
        using var rig = new Rig(threshold: 3);
        for (var i = 0; i < 6; i++)
        {
            rig.Frame.Matrix = new RenderTransform(0.5, 0, 20 + i, 0, 0.5, 30, 0, 0, 1);
            rig.Commit(1);
        }

        Assert.Equal(0, rig.Scene.OffloadCommits);
        Assert.True(rig.Scene.DeclinedFor(PlaneDeclineReason.Settling) > 0);
    }

    [Fact]
    public void A_refused_layer_is_not_offered_again_until_its_destination_size_changes()
    {
        using var rig = new Rig();
        rig.Output.Accept = (_, _) => false;
        rig.Frame.Matrix = new RenderTransform(0.5, 0, 20, 0, 0.5, 30, 0, 0, 1);
        rig.Commit(1);
        Assert.NotEmpty(rig.Offered);

        rig.Offered.Clear();
        rig.Commit(3);
        Assert.Empty(rig.Offered);
        Assert.True(rig.Scene.DeclinedFor(PlaneDeclineReason.BackendRefused) > 0);

        rig.Frame.Matrix = new RenderTransform(0.2, 0, 20, 0, 0.2, 30, 0, 0, 1);
        rig.Commit(1);
        Assert.NotEmpty(rig.Offered);
        Assert.All(rig.Offered, offered => Assert.Equal(8, offered.DstBox.Width));
    }

    private sealed class Rig : IDisposable
    {
        private readonly CompositorTestHost _host = new();
        private readonly Swapchain _swapchain = new(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        private readonly OutputState _state = new();
        private readonly SceneRect _background;
        private readonly SceneBuffer _node;
        private readonly BufferBase _client;
        private int _frames;

        public Rig(int threshold = 1)
        {
            Output = new PlaneOutput();
            using (var lit = new OutputState())
            {
                Assert.True(Output.Commit(lit.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000))));
            }

            Scene = new SceneOutput(_host.Scene, Output) { OffloadEntryThreshold = threshold };
            _background = new SceneRect(_host.Scene.Root, 160, 120, new RenderColor(0.1f, 0.2f, 0.3f, 1f));
            Frame = new SceneTransform(_host.Scene.Root);
            _client = DirectScanoutTests.FakeClientBuffer(40, 40);
            _node = new SceneBuffer(Frame);
            _node.SetBuffer(_client);
            _node.SetPosition(10, 10);
            Output.Accept = (_, _) => true;
            Output.Seen = Offered;
        }

        public PlaneOutput Output { get; }

        public SceneOutput Scene { get; }

        public SceneTransform Frame { get; }

        public List<OutputLayer> Offered { get; } = [];

        public void Commit(int frames)
        {
            var options = new SceneCommitOptions { AllowPlaneOffload = true };
            for (var i = 0; i < frames; i++)
            {
                _background.SetPosition(_frames++ % 2, 0);
                Assert.True(Scene.Commit(_host.Renderer, _swapchain, _state, options));
            }
        }

        public void Dispose()
        {
            Scene.Dispose();
            _node.Destroy();
            Frame.Destroy();
            _background.Destroy();
            _client.Destroy();
            Output.Destroy();
            _state.Dispose();
            _swapchain.Dispose();
            _host.Dispose();
        }
    }

    private sealed class PlaneOutput() : OutputBase("plane-scale-test")
    {
        public Func<OutputLayer, int, bool>? Accept { get; set; }

        public List<OutputLayer>? Seen { get; set; }

        protected override bool SupportsLayers => true;

        protected override bool TestCommitCore(OutputState state) => Judge(state, test: true);

        protected override bool CommitCore(OutputState state) => Judge(state, test: false);

        private bool Judge(OutputState state, bool test)
        {
            if ((state.Fields & OutputStateFields.Layers) == 0 || state.Layers is null)
            {
                return true;
            }

            for (var i = 0; i < state.Layers.Count; i++)
            {
                var layer = state.Layers[i];
                if (test && layer.Buffer is not null)
                {
                    Seen?.Add(new OutputLayer { DstBox = layer.DstBox, SrcBox = layer.SrcBox, Buffer = layer.Buffer });
                }

                layer.Accepted = Accept?.Invoke(layer, i) ?? false;
            }

            return true;
        }
    }
}
