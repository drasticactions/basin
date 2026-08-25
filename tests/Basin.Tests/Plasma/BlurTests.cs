using Basin.Capabilities;
using Basin.Plasma;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class BlurTests
{
    private sealed class Backend : IBackgroundEffects
    {
        public BackgroundEffects Supported => BackgroundEffects.Blur;
    }

    private sealed class BlurFixture : IDisposable
    {
        public required BlurManager Manager;
        public required Basin.Plasma.Protocol.OrgKdeKwinBlurManager Proxy;

        public void Dispose() => Manager.Dispose();
    }

    private static BlurFixture Start(CompositorTestHost host, IBackgroundEffects? effects)
    {
        var manager = new BlurManager(host.Display, host.Compositor, effects);
        Basin.Plasma.Protocol.OrgKdeKwinBlurManager? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_blur_manager")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinBlurManager>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return new BlurFixture { Manager = manager, Proxy = proxy! };
    }

    private static void AssertClientAlive(CompositorTestHost host)
    {
        var sync = host.Client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
    }

    [Fact]
    public void A_region_stages_on_the_blur_object_and_applies_on_the_surface_commit()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, new Backend());
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Last();

        var blur = fixture.Proxy.Create(surface);
        var region = host.Client.Compositor.CreateRegion();
        region.Add(4, 8, 20, 30);
        blur.SetRegion(region);
        region.Destroy();
        surface.Commit();
        host.PumpToServer();

        Assert.NotNull(fixture.Manager.BlurOf(server));
        Assert.True(fixture.Manager.BlurOf(server)!.WholeSurface);

        blur.Commit();
        surface.Commit();
        host.PumpToServer();

        var recorded = fixture.Manager.BlurOf(server);
        Assert.NotNull(recorded);
        Assert.False(recorded!.WholeSurface);
        var extents = recorded.Region.Extents;
        Assert.Equal(4, extents.X1);
        Assert.Equal(8, extents.Y1);
        Assert.Equal(24, extents.X2);
        Assert.Equal(38, extents.Y2);
        Assert.Same(recorded.Region, fixture.Manager.BlurRegionOf(server));
        AssertClientAlive(host);
    }

    [Fact]
    public void An_unset_region_means_the_whole_surface_rather_than_none()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, new Backend());
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Last();

        var blur = fixture.Proxy.Create(surface);
        blur.SetRegion(null);
        blur.Commit();
        surface.Commit();
        host.PumpToServer();

        var recorded = fixture.Manager.BlurOf(server);
        Assert.NotNull(recorded);
        Assert.True(recorded!.WholeSurface);
        Assert.Null(fixture.Manager.BlurRegionOf(server));
        AssertClientAlive(host);
    }

    [Fact]
    public void Unset_drops_the_blur_from_the_next_commit()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, new Backend());
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Last();

        var blur = fixture.Proxy.Create(surface);
        var region = host.Client.Compositor.CreateRegion();
        region.Add(0, 0, 10, 10);
        blur.SetRegion(region);
        region.Destroy();
        blur.Commit();
        surface.Commit();
        host.PumpToServer();
        Assert.NotNull(fixture.Manager.BlurOf(server));

        fixture.Proxy.Unset(surface);
        surface.Commit();
        host.PumpToServer();
        Assert.Null(fixture.Manager.BlurOf(server));
        AssertClientAlive(host);
    }

    [Fact]
    public void Releasing_the_object_drops_the_region()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, new Backend());
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Last();

        var blur = fixture.Proxy.Create(surface);
        var region = host.Client.Compositor.CreateRegion();
        region.Add(0, 0, 10, 10);
        blur.SetRegion(region);
        region.Destroy();
        blur.Commit();
        surface.Commit();
        host.PumpToServer();
        Assert.NotNull(fixture.Manager.BlurOf(server));

        blur.Release();
        host.PumpToServer();
        Assert.Null(fixture.Manager.BlurOf(server));
        AssertClientAlive(host);
    }
}
