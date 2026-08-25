using Basin.Capabilities;
using Basin.Plasma;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ContrastTests
{
    private sealed class Backend : IBackgroundEffects
    {
        public BackgroundEffects Supported => BackgroundEffects.Contrast;
    }

    private sealed class ContrastFixture : IDisposable
    {
        public required ContrastManager Manager;
        public required Basin.Plasma.Protocol.OrgKdeKwinContrastManager Proxy;

        public void Dispose() => Manager.Dispose();
    }

    private static ContrastFixture Start(CompositorTestHost host, IBackgroundEffects? effects, uint version = 2)
    {
        var manager = new ContrastManager(host.Display, host.Compositor, effects);
        Basin.Plasma.Protocol.OrgKdeKwinContrastManager? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_contrast_manager")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinContrastManager>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return new ContrastFixture { Manager = manager, Proxy = proxy! };
    }

    private static Surface Server(CompositorTestHost host) => host.Compositor.Surfaces.Last();

    private static void AssertClientAlive(CompositorTestHost host)
    {
        var sync = host.Client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
    }

    [Fact]
    public void The_three_numbers_and_the_region_apply_on_the_surface_commit()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, new Backend());
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = Server(host);

        var contrast = fixture.Proxy.Create(surface);
        var region = host.Client.Compositor.CreateRegion();
        region.Add(2, 4, 40, 50);
        contrast.SetRegion(region);
        region.Destroy();
        contrast.SetContrast(WlFixed.FromDouble(1.25));
        contrast.SetIntensity(WlFixed.FromDouble(0.9));
        contrast.SetSaturation(WlFixed.FromDouble(1.7));
        contrast.Commit();
        surface.Commit();
        host.PumpToServer();

        Assert.True(fixture.Manager.TryGetContrast(server, out var parameters));
        Assert.Equal(1.25, parameters.Contrast, 2);
        Assert.Equal(0.9, parameters.Intensity, 2);
        Assert.Equal(1.7, parameters.Saturation, 2);
        Assert.False(parameters.Frost);

        var recorded = fixture.Manager.ContrastRegionOf(server);
        Assert.NotNull(recorded);
        var extents = recorded!.Extents;
        Assert.Equal(2, extents.X1);
        Assert.Equal(42, extents.X2);
        AssertClientAlive(host);
    }

    [Fact]
    public void Frost_records_the_colour_and_unset_drops_it()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, new Backend());
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = Server(host);

        var contrast = fixture.Proxy.Create(surface);
        contrast.SetFrost(0x20, 0x40, 0x60, 0x80);
        contrast.Commit();
        surface.Commit();
        host.PumpToServer();

        Assert.True(fixture.Manager.TryGetContrast(server, out var frosted));
        Assert.True(frosted.Frost);
        Assert.Equal(0x80204060u, frosted.FrostColor);

        contrast.UnsetFrost();
        contrast.Commit();
        surface.Commit();
        host.PumpToServer();

        Assert.True(fixture.Manager.TryGetContrast(server, out var plain));
        Assert.False(plain.Frost);
        Assert.Equal(0u, plain.FrostColor);
        AssertClientAlive(host);
    }

    [Fact]
    public void An_unset_region_means_the_whole_surface()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, new Backend());
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = Server(host);

        var contrast = fixture.Proxy.Create(surface);
        contrast.SetRegion(null);
        contrast.Commit();
        surface.Commit();
        host.PumpToServer();

        Assert.True(fixture.Manager.ContrastOf(server)!.WholeSurface);
        Assert.Null(fixture.Manager.ContrastRegionOf(server));
        AssertClientAlive(host);
    }

    [Fact]
    public void Without_a_backend_nothing_is_reported_and_the_client_survives()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, effects: null);
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = Server(host);

        var contrast = fixture.Proxy.Create(surface);
        contrast.SetContrast(WlFixed.FromDouble(2.0));
        contrast.Commit();
        surface.Commit();
        host.PumpToServer();

        Assert.NotNull(fixture.Manager.ContrastOf(server));
        Assert.False(fixture.Manager.TryGetContrast(server, out var parameters));
        Assert.Equal(1.0, parameters.Contrast, 2);
        Assert.Null(fixture.Manager.ContrastRegionOf(server));
        AssertClientAlive(host);
    }

    [Fact]
    public void A_version_one_client_never_sees_the_frost_requests()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, new Backend(), version: 1);
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = Server(host);

        var contrast = fixture.Proxy.Create(surface);
        contrast.SetSaturation(WlFixed.FromDouble(0.5));
        contrast.Commit();
        surface.Commit();
        host.PumpToServer();

        Assert.True(fixture.Manager.TryGetContrast(server, out var parameters));
        Assert.Equal(0.5, parameters.Saturation, 2);
        Assert.False(parameters.Frost);
        AssertClientAlive(host);
    }
}
