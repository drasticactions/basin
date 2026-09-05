using Basin.Capabilities;
using Basin.Desktop;
using Xunit;

namespace Basin.Tests;

public sealed class SurfaceLutDriverTests
{
    private static readonly ImageDescription Pq = new()
    {
        PrimariesNamed = ColorPrimaries.Bt2020,
        TransferNamed = ColorTransferFunction.St2084Pq,
    };

    private static readonly ImageDescription P3 = new()
    {
        PrimariesNamed = ColorPrimaries.DisplayP3,
        TransferNamed = ColorTransferFunction.Gamma22,
    };

    [Fact]
    public void Resolve_runs_when_a_description_changes_and_never_per_frame()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor);
        _ = host.SceneOutput;
        var resolver = new CountingResolver();
        var driver = new SurfaceLutDriver(host.Scene, manager, resolver);
        Assert.Equal(1, resolver.Calls);

        _ = MappedToplevel.Map(host, host.Client);
        driver.Refresh();
        Assert.Equal(1, resolver.Calls);

        for (var i = 0; i < 10; i++)
        {
            host.CommitFrame();
        }

        Assert.Equal(1, resolver.Calls);

        host.SceneOutput.ColorDescription = Pq;
        Assert.Equal(2, resolver.Calls);
        driver.Refresh();
        Assert.Equal(2, resolver.Calls);
    }

    [Fact]
    public void A_surface_resolves_against_every_output_in_its_own_space()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor);
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        using var secondGlobal = new OutputGlobal(host.Display, second);
        using var secondScene = new Basin.Scene.SceneOutput(host.Scene, second);
        host.SceneOutput.ColorDescription = P3;
        secondScene.ColorDescription = Pq;

        var resolver = new CountingResolver();
        var driver = new SurfaceLutDriver(host.Scene, manager, resolver);
        driver.WatchToplevels(host.Shell);
        var window = MappedToplevel.Map(host, host.Client);

        Assert.Contains((ImageDescription.SdrDefault, P3), resolver.Asked);
        Assert.Contains((ImageDescription.SdrDefault, Pq), resolver.Asked);

        var node = Assert.Single(host.SurfaceScenes, s => s.Surface == window.ServerSurface).Content;
        Assert.NotNull(node);
        Assert.Null(node.ColorDescription);
        Assert.NotNull(host.SceneOutput.LutFor(node));
        Assert.NotNull(secondScene.LutFor(node));

        resolver.Asked.Clear();
        host.SceneOutput.ColorDescription = ImageDescription.SdrDefault;
        Assert.Contains((ImageDescription.SdrDefault, ImageDescription.SdrDefault), resolver.Asked);
        Assert.Null(host.SceneOutput.LutFor(node));
        Assert.NotNull(secondScene.LutFor(node));
        host.PumpToServer();
    }

    private sealed class CountingResolver : IColorTransformResolver
    {
        private readonly FakeLut _lut = new();

        public int Calls { get; private set; }

        public List<(ImageDescription Source, ImageDescription Output)> Asked { get; } = [];

        public ColorTransformCapability Capability => ColorTransformCapability.Lut3D;

        public IColorLut? Resolve(ImageDescription source, ImageDescription output)
        {
            Calls++;
            Asked.Add((source, output));
            return ImageDescription.ContentComparer.Equals(source, output) ? null : _lut;
        }

        private sealed class FakeLut : IColorLut
        {
            public void Dispose()
            {
            }
        }
    }
}
