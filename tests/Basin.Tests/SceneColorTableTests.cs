using Basin.Capabilities;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class SceneColorTableTests
{
    private static readonly ImageDescription P3 = new()
    {
        PrimariesNamed = ColorPrimaries.DisplayP3,
        TransferNamed = ColorTransferFunction.Gamma22,
    };

    private static readonly ImageDescription Pq = new()
    {
        PrimariesNamed = ColorPrimaries.Bt2020,
        TransferNamed = ColorTransferFunction.St2084Pq,
    };

    [Fact]
    public void Each_output_resolves_a_node_against_its_own_description_and_keeps_its_plane()
    {
        using var host = new CompositorTestHost();
        var outputA = new PlaneOutput();
        var outputB = new PlaneOutput();
        using (var lit = new OutputState())
        {
            Assert.True(outputA.Commit(lit.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000))));
        }

        using (var lit = new OutputState())
        {
            Assert.True(outputB.Commit(lit.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000))));
        }

        using var resolver = new PairResolver(host.Renderer);
        host.Scene.ColorTransforms = resolver;
        using var sceneA = new SceneOutput(host.Scene, outputA) { OffloadEntryThreshold = 1, ColorDescription = P3 };
        using var sceneB = new SceneOutput(host.Scene, outputB) { OffloadEntryThreshold = 1, ColorDescription = Pq };
        using var swapchainA = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var swapchainB = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var stateA = new OutputState();
        using var stateB = new OutputState();
        var options = new SceneCommitOptions { AllowPlaneOffload = true };

        var background = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0.1f, 0.2f, 0.3f, 1f));
        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        using var clientGuard = new DestroyAtExit(client);
        var node = new SceneBuffer(host.Scene.Root) { ColorDescription = P3 };
        node.SetBuffer(client);
        node.SetPosition(10, 10);
        outputA.Accept = (_, _) => true;
        outputB.Accept = (_, _) => true;

        Assert.Null(sceneA.LutFor(node));
        Assert.NotNull(sceneB.LutFor(node));
        Assert.Contains((P3, Pq), resolver.Asked);
        Assert.DoesNotContain((Pq, P3), resolver.Asked);

        for (var i = 0; i < 4; i++)
        {
            background.SetPosition(i % 2, 0);
            Assert.True(sceneA.Commit(host.Renderer, swapchainA, stateA, options));
            Assert.True(sceneB.Commit(host.Renderer, swapchainB, stateB, options));
        }

        Assert.True(sceneA.OffloadCommits > 0, "the matching output offloads the node");
        Assert.Equal(0, sceneA.DeclinedFor(PlaneDeclineReason.ColorTransform));
        Assert.Equal(0, sceneB.OffloadCommits);
        Assert.True(sceneB.DeclinedFor(PlaneDeclineReason.ColorTransform) > 0, "the converting output declines the plane");

        var lutOnB = sceneB.LutFor(node);
        sceneA.Dispose();
        Assert.Same(lutOnB, sceneB.LutFor(node));
        background.SetPosition(5, 0);
        Assert.True(sceneB.Commit(host.Renderer, swapchainB, stateB, options));
        Assert.True(sceneB.ComposedCommits > 0);

        node.Destroy();
        background.Destroy();
        outputA.Destroy();
        outputB.Destroy();
    }

    [Fact]
    public void A_description_set_after_the_output_exists_is_resolved_without_a_rebuild()
    {
        using var host = new CompositorTestHost();
        using var resolver = new PairResolver(host.Renderer);
        host.Scene.ColorTransforms = resolver;
        host.SceneOutput.ColorDescription = Pq;

        var tree = new SceneTree(host.Scene.Root);
        var node = new SceneBuffer(tree);
        Assert.NotNull(host.SceneOutput.LutFor(node));
        var asked = resolver.Asked.Count;

        node.ColorDescription = Pq;
        Assert.Null(host.SceneOutput.LutFor(node));
        Assert.Equal(asked + 1, resolver.Asked.Count);

        node.ColorDescription = P3;
        Assert.NotNull(host.SceneOutput.LutFor(node));

        var content = RendererLutTestsSolid(60, 40);
        node.SetBuffer(content);
        asked = resolver.Asked.Count;
        var snapshot = SceneSnapshot.Capture(tree, host.Scene.Root);
        Assert.Equal(1, snapshot.NodeCount);
        var copy = Assert.IsType<SceneBuffer>(Assert.Single(snapshot.Tree.Children));
        Assert.Same(P3, copy.ColorDescription);
        Assert.Same(host.SceneOutput.LutFor(node), host.SceneOutput.LutFor(copy));
        Assert.Equal(asked, resolver.Asked.Count);
        snapshot.Destroy();
        node.Destroy();
        tree.Destroy();
        content.Destroy();
    }

    private static MemoryBuffer RendererLutTestsSolid(int width, int height)
    {
        var buffer = new MemoryBuffer(width, height, DrmFormat.Argb8888);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out _));
        buffer.EndDataAccess();
        return buffer;
    }

    internal sealed class PairResolver(IRenderer renderer) : IColorTransformResolver, IDisposable
    {
        private readonly Dictionary<(ImageDescription, ImageDescription), IColorLut?> _luts = [];

        public List<(ImageDescription Source, ImageDescription Output)> Asked { get; } = [];

        public ColorTransformCapability Capability => renderer.ColorTransform;

        public IColorLut? Resolve(ImageDescription source, ImageDescription output)
        {
            Asked.Add((source, output));
            if (ImageDescription.ContentComparer.Equals(source, output))
            {
                return null;
            }

            if (!_luts.TryGetValue((source, output), out var lut))
            {
                lut = renderer.ColorTransform == ColorTransformCapability.None ? null : renderer.ImportLut(Identity());
                _luts[(source, output)] = lut;
            }

            return lut;
        }

        public void Dispose()
        {
            foreach (var lut in _luts.Values)
            {
                lut?.Dispose();
            }
        }

        private static ColorLut3D Identity()
        {
            var data = new float[2 * 2 * 2 * 3];
            var index = 0;
            for (var b = 0; b < 2; b++)
            {
                for (var g = 0; g < 2; g++)
                {
                    for (var r = 0; r < 2; r++)
                    {
                        data[index++] = r;
                        data[index++] = g;
                        data[index++] = b;
                    }
                }
            }

            return new ColorLut3D(2, data);
        }
    }

    private sealed class DestroyAtExit(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }

    private sealed class PlaneOutput() : OutputBase("plane-color-test")
    {
        public Func<OutputLayer, int, bool>? Accept { get; set; }

        protected override bool SupportsLayers => true;

        protected override bool TestCommitCore(OutputState state) => Judge(state);

        protected override bool CommitCore(OutputState state) => Judge(state);

        private bool Judge(OutputState state)
        {
            if ((state.Fields & OutputStateFields.Layers) == 0 || state.Layers is null)
            {
                return true;
            }

            for (var i = 0; i < state.Layers.Count; i++)
            {
                state.Layers[i].Accepted = Accept?.Invoke(state.Layers[i], i) ?? false;
            }

            return true;
        }
    }
}
