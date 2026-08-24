using Basin.Desktop;
using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class AppMenuTests
{
    private static Basin.Plasma.Protocol.OrgKdeKwinAppmenuManager BindManager(
        CompositorTestHost host, ShmTestClient? client = null, uint version = 2)
    {
        client ??= host.Client;
        Basin.Plasma.Protocol.OrgKdeKwinAppmenuManager? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_appmenu_manager")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinAppmenuManager>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        host.PumpToServer();
        return proxy!;
    }

    [Fact]
    public void Set_address_records_both_strings_and_raises_once()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.AppMenuManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        var raised = new List<(string Service, string Path)>();
        manager.AddressChanged += (_, service, path) => raised.Add((service, path));

        var factory = BindManager(host);
        var appMenu = factory.Create(window.Surface);
        appMenu.SetAddress("org.kde.kate", "/MenuBar");
        host.PumpToServer();

        Assert.Equal(("org.kde.kate", "/MenuBar"), Assert.Single(raised));
        var entry = manager.MenuOf(window.ServerSurface);
        Assert.NotNull(entry);
        Assert.Equal("org.kde.kate", entry!.ServiceName);
        Assert.Equal("/MenuBar", entry.ObjectPath);
    }

    [Fact]
    public void A_second_set_address_replaces_both_strings()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.AppMenuManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        var raised = new List<(string Service, string Path)>();
        manager.AddressChanged += (_, service, path) => raised.Add((service, path));

        var factory = BindManager(host);
        var appMenu = factory.Create(window.Surface);
        appMenu.SetAddress("org.kde.kate", "/MenuBar");
        appMenu.SetAddress("org.kde.dolphin", "/Menu");
        host.PumpToServer();

        Assert.Equal(2, raised.Count);
        Assert.Equal(("org.kde.dolphin", "/Menu"), raised[^1]);
        Assert.Equal("org.kde.dolphin", manager.MenuOf(window.ServerSurface)!.ServiceName);
    }

    [Fact]
    public void Destroying_the_appmenu_object_clears_the_entry()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.AppMenuManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        var raised = new List<(string Service, string Path)>();
        manager.AddressChanged += (_, service, path) => raised.Add((service, path));

        var factory = BindManager(host);
        var appMenu = factory.Create(window.Surface);
        appMenu.SetAddress("org.kde.kate", "/MenuBar");
        host.PumpToServer();
        Assert.NotNull(manager.MenuOf(window.ServerSurface));

        appMenu.Release();
        host.PumpToServer();

        Assert.Null(manager.MenuOf(window.ServerSurface));
        Assert.Equal((string.Empty, string.Empty), raised[^1]);
    }

    [Fact]
    public void Destroying_the_surface_clears_the_entry()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.AppMenuManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        var factory = BindManager(host);
        var appMenu = factory.Create(window.Surface);
        appMenu.SetAddress("org.kde.kate", "/MenuBar");
        host.PumpToServer();
        var surface = window.ServerSurface;
        Assert.NotNull(manager.MenuOf(surface));

        window.Toplevel.Dispose();
        window.XdgSurface.Dispose();
        window.Surface.Dispose();
        host.PumpToServer();

        Assert.Null(manager.MenuOf(surface));
    }

    [Fact]
    public void Two_surfaces_from_one_client_hold_independent_addresses()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.AppMenuManager(host.Display, host.Compositor);
        var first = MappedToplevel.Map(host, host.Client);
        var second = MappedToplevel.Map(host, host.Client);

        var factory = BindManager(host);
        factory.Create(first.Surface).SetAddress("org.kde.kate", "/MenuBar");
        factory.Create(second.Surface).SetAddress("org.kde.dolphin", "/Menu");
        host.PumpToServer();

        Assert.Equal("org.kde.kate", manager.MenuOf(first.ServerSurface)!.ServiceName);
        Assert.Equal("org.kde.dolphin", manager.MenuOf(second.ServerSurface)!.ServiceName);
    }

    [Fact]
    public void The_wired_address_reaches_plasma_window_management_at_ten_and_above()
    {
        using var host = new CompositorTestHost();
        using var source = new XdgToplevelSource(host.Shell);
        var model = new Basin.Capabilities.AggregateToplevelModel();
        model.Add(source);
        using var windows = new PlasmaWindowManager(host.Display, model, workspaces: null);
        using var manager = new Basin.Plasma.AppMenuManager(host.Display, host.Compositor);

        var window = MappedToplevel.Map(host, host.Client);
        manager.AddressChanged += (_, service, path) =>
            source.SetAppMenu(window.ServerToplevel, service, path);

        var announcedModern = new List<(uint Id, string Uuid)>();
        var announcedLegacy = new List<uint>();
        Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement? modern = null;
        Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement? legacy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_plasma_window_management")
            {
                modern = registry.Bind<Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement>(e.Name, 20);
                modern.WindowWithUuid += (_, we) => announcedModern.Add((we.Id, we.Uuid));
                legacy = registry.Bind<Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement>(e.Name, 9);
                legacy.Window += (_, we) => announcedLegacy.Add(we.Id);
            }
        };
        host.PumpToClient();
        host.PumpToClient();
        Assert.Single(announcedModern);
        Assert.Single(announcedLegacy);

        var modernWindow = modern!.GetWindowByUuid(announcedModern[0].Uuid);
        var legacyWindow = legacy!.GetWindow(announcedLegacy[0]);
        var modernMenus = new List<(string? Service, string? Path)>();
        var legacyMenus = new List<(string? Service, string? Path)>();
        modernWindow.ApplicationMenu += (_, e) => modernMenus.Add((e.ServiceName, e.ObjectPath));
        legacyWindow.ApplicationMenu += (_, e) => legacyMenus.Add((e.ServiceName, e.ObjectPath));
        host.PumpToServer();

        var factory = BindManager(host);
        factory.Create(window.Surface).SetAddress("org.kde.kate", "/MenuBar");
        host.PumpToClient();

        Assert.Equal(("org.kde.kate", "/MenuBar"), Assert.Single(modernMenus)!);
        Assert.Empty(legacyMenus);
    }

    [Fact]
    public void A_version_one_client_is_cleaned_up_on_disconnect()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.AppMenuManager(host.Display, host.Compositor);
        var other = host.ConnectClient();
        var window = MappedToplevel.Map(host, other);

        var factory = BindManager(host, other, version: 1);
        var appMenu = factory.Create(window.Surface);
        appMenu.SetAddress("org.kde.kate", "/MenuBar");
        host.PumpToServer();
        var surface = window.ServerSurface;
        Assert.NotNull(manager.MenuOf(surface));

        host.DisconnectClient(other);
        host.PumpToServer();
        host.PumpToServer();

        Assert.Null(manager.MenuOf(surface));

        var sync = host.Client.Display.Sync();
        var alive = false;
        sync.Done += (_, _) => alive = true;
        host.PumpUntil(() => alive);
    }
}
