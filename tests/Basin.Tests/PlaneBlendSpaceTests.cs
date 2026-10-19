using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class PlaneBlendSpaceTests
{
    [Theory]
    [InlineData(RenderBlendSpace.Encoded, false, true)]
    [InlineData(RenderBlendSpace.Linear, false, false)]
    [InlineData(RenderBlendSpace.Linear, true, true)]
    public void A_translucent_buffer_reaches_a_plane_only_where_the_planes_blend_alike(RenderBlendSpace space, bool opaque, bool offered)
    {
        using var host = new CompositorTestHost();
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var output = new PlaneOutput();
        using (var lit = new OutputState())
        {
            Assert.True(output.Commit(lit.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000))));
        }

        var renderer = new BlendSpaceRenderer(host.Renderer, space);
        using var scene = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        var background = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0.1f, 0.2f, 0.3f, 1f));
        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root) { IsOpaque = opaque, DebugName = "probe" };
        node.SetBuffer(client);
        node.SetPosition(10, 10);
        try
        {
            var options = new SceneCommitOptions { AllowPlaneOffload = true };
            for (var i = 0; i < 4; i++)
            {
                background.SetPosition(i % 2, 0);
                Assert.True(scene.Commit(renderer, swapchain, state, options));
            }

            Assert.Equal(offered, output.Offered > 0);
            Assert.Equal(!offered, scene.DeclinedFor(PlaneDeclineReason.BlendSpace) > 0);
            var report = SceneDiagnostics.Report(scene);
            Assert.Null(report.Skipped);
            var probe = report.Entries.Where(entry => entry.Path == "tree/probe").ToList();
            Assert.Equal(!offered, probe.Any(entry => !entry.OnPlane && entry.Reason == "blend-space"));
            Assert.Equal(offered, probe.Any(entry => entry.OnPlane));
        }
        finally
        {
            node.Destroy();
            background.Destroy();
            client.Destroy();
            output.Destroy();
        }
    }

    [Theory]
    [InlineData("pixman", RenderBlendSpace.Encoded)]
    [InlineData("gl", RenderBlendSpace.Encoded)]
    [InlineData("vulkan", RenderBlendSpace.Linear)]
    [InlineData("skia", RenderBlendSpace.Encoded)]
    [InlineData("skia-gl", RenderBlendSpace.Encoded)]
    [InlineData("skia-vulkan", RenderBlendSpace.Encoded)]
    [InlineData("skia-graphite", RenderBlendSpace.Encoded)]
    public void Each_row_declares_the_space_it_blends_in(string row, RenderBlendSpace space)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = Basin.Renderers.RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        try
        {
            Assert.Equal(space, stack.Renderer.BlendSpace);
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private sealed class BlendSpaceRenderer(IRenderer inner, RenderBlendSpace space) : IRenderer
    {
        public RenderBlendSpace BlendSpace => space;

        public ITexture? ImportTexture(IBuffer buffer) => inner.ImportTexture(buffer);

        public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options) => inner.BeginBufferPass(target, options);

        public void Dispose()
        {
        }
    }

    private sealed class PlaneOutput() : OutputBase("plane-blend-test")
    {
        public int Offered { get; private set; }

        protected override bool SupportsLayers => true;

        protected override bool TestCommitCore(OutputState state) => Judge(state, test: true);

        protected override bool CommitCore(OutputState state) => Judge(state, test: false);

        private bool Judge(OutputState state, bool test)
        {
            if ((state.Fields & OutputStateFields.Layers) == 0 || state.Layers is null)
            {
                return true;
            }

            foreach (var layer in state.Layers)
            {
                if (test && layer.Buffer is not null)
                {
                    Offered++;
                }

                layer.Accepted = true;
            }

            return true;
        }
    }
}
