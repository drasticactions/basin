using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class ServerDecorationPaletteTests
{
    private static Basin.Plasma.Protocol.OrgKdeKwinServerDecorationPaletteManager BindManager(
        CompositorTestHost host, ShmTestClient? client = null)
    {
        client ??= host.Client;
        Basin.Plasma.Protocol.OrgKdeKwinServerDecorationPaletteManager? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_server_decoration_palette_manager")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinServerDecorationPaletteManager>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        host.PumpToServer();
        return proxy!;
    }

    [Fact]
    public void Set_palette_records_the_string_and_raises_once()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ServerDecorationPaletteManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        var raised = new List<string?>();
        manager.PaletteChanged += (_, palette) => raised.Add(palette);

        var factory = BindManager(host);
        factory.Create(window.Surface).SetPalette("BreezeDark");
        host.PumpToServer();

        Assert.Equal("BreezeDark", Assert.Single(raised));
        Assert.Equal("BreezeDark", manager.PaletteOf(window.ServerSurface)!.Palette);
    }

    [Fact]
    public void A_second_set_palette_replaces_the_first()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ServerDecorationPaletteManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        var factory = BindManager(host);
        var palette = factory.Create(window.Surface);
        palette.SetPalette("BreezeDark");
        palette.SetPalette("/home/user/.local/share/color-schemes/Custom.colors");
        host.PumpToServer();

        Assert.Equal(
            "/home/user/.local/share/color-schemes/Custom.colors",
            manager.PaletteOf(window.ServerSurface)!.Palette);
    }

    [Fact]
    public void Release_clears_the_entry()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ServerDecorationPaletteManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        var raised = new List<string?>();
        manager.PaletteChanged += (_, palette) => raised.Add(palette);

        var factory = BindManager(host);
        var palette = factory.Create(window.Surface);
        palette.SetPalette("BreezeDark");
        host.PumpToServer();
        Assert.NotNull(manager.PaletteOf(window.ServerSurface));

        palette.Release();
        host.PumpToServer();

        Assert.Null(manager.PaletteOf(window.ServerSurface));
        Assert.Null(raised[^1]);
    }

    [Fact]
    public void Destroying_the_surface_clears_the_entry()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ServerDecorationPaletteManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        var factory = BindManager(host);
        factory.Create(window.Surface).SetPalette("BreezeDark");
        host.PumpToServer();
        var surface = window.ServerSurface;
        Assert.NotNull(manager.PaletteOf(surface));

        window.Toplevel.Dispose();
        window.XdgSurface.Dispose();
        window.Surface.Dispose();
        host.PumpToServer();

        Assert.Null(manager.PaletteOf(surface));
    }

    [Fact]
    public void Two_surfaces_from_one_client_hold_independent_palettes()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ServerDecorationPaletteManager(host.Display, host.Compositor);
        var first = MappedToplevel.Map(host, host.Client);
        var second = MappedToplevel.Map(host, host.Client);

        var factory = BindManager(host);
        factory.Create(first.Surface).SetPalette("BreezeDark");
        factory.Create(second.Surface).SetPalette("BreezeLight");
        host.PumpToServer();

        Assert.Equal("BreezeDark", manager.PaletteOf(first.ServerSurface)!.Palette);
        Assert.Equal("BreezeLight", manager.PaletteOf(second.ServerSurface)!.Palette);
    }

    [Fact]
    public void The_string_reaches_frame_state_when_wired_and_is_null_when_not()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ServerDecorationPaletteManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        var unwired = new FrameState { Title = window.ServerToplevel.Title };
        Assert.Null(unwired.Palette);

        var factory = BindManager(host);
        factory.Create(window.Surface).SetPalette("BreezeDark");
        host.PumpToServer();

        var wired = new FrameState
        {
            Title = window.ServerToplevel.Title,
            Palette = manager.PaletteOf(window.ServerSurface)?.Palette,
        };
        Assert.Equal("BreezeDark", wired.Palette);
    }

    [Fact]
    public void A_disconnecting_client_leaves_nothing_behind()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ServerDecorationPaletteManager(host.Display, host.Compositor);
        var other = host.ConnectClient();
        var window = MappedToplevel.Map(host, other);

        var factory = BindManager(host, other);
        factory.Create(window.Surface).SetPalette("BreezeDark");
        host.PumpToServer();
        var surface = window.ServerSurface;
        Assert.NotNull(manager.PaletteOf(surface));

        host.DisconnectClient(other);
        host.PumpToServer();
        host.PumpToServer();

        Assert.Null(manager.PaletteOf(surface));

        var sync = host.Client.Display.Sync();
        var alive = false;
        sync.Done += (_, _) => alive = true;
        host.PumpUntil(() => alive);
    }
}
