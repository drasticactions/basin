using Basin.Plasma;
using Basin.Shell.Xdg;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class PlasmaShellTests
{
    private sealed class ShellFixture : IDisposable
    {
        public required PlasmaShellManager Manager;
        public required PlasmaShellPlacement Placement;
        public required PlasmaScreenEdges Edges;
        public required Basin.Plasma.Protocol.OrgKdePlasmaShell Proxy;

        public void Dispose()
        {
            Manager.Dispose();
            Edges.Dispose();
            Placement.Dispose();
        }
    }

    private static ShellFixture Start(
        CompositorTestHost host, bool withSeat = true, XdgToplevelSource? toplevels = null)
    {
        var placement = new PlasmaShellPlacement(host.Scene, host.Layout)
        {
            Seat = withSeat ? host.Seat : null,
        };
        var edges = new PlasmaScreenEdges(host.Loop, withSeat ? host.Seat : null, host.Layout);
        placement.ScreenEdges = edges;
        var manager = new PlasmaShellManager(host.Display, host.Compositor, toplevels);
        placement.Attach(manager);

        Basin.Plasma.Protocol.OrgKdePlasmaShell? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_plasma_shell")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdePlasmaShell>(e.Name, 8);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return new ShellFixture { Manager = manager, Placement = placement, Edges = edges, Proxy = proxy! };
    }

    private static (WlSurface Surface, Basin.Plasma.Protocol.OrgKdePlasmaSurface Plasma) CreateSurface(
        CompositorTestHost host, ShellFixture fixture)
    {
        var surface = host.Client.Compositor.CreateSurface();
        var plasma = fixture.Proxy.GetSurface(surface);
        host.PumpToServer();
        return (surface, plasma);
    }

    private static void CommitBuffer(
        CompositorTestHost host, WlSurface surface, int width = 40, int height = 30)
    {
        var buffer = host.Client.CreateBuffer(width, height, Fill.Solid(width, height, 0xFF204060));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        host.PumpToServer();
    }

    private static PlasmaShellSurface Tracked(ShellFixture fixture)
    {
        Assert.Single(fixture.Manager.Surfaces);
        return fixture.Manager.Surfaces[0];
    }

    [Fact]
    public void A_second_set_role_is_ignored_without_an_error()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, plasma) = CreateSurface(host, fixture);

        plasma.SetRole((uint)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Panel);
        plasma.SetRole((uint)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Desktop);
        host.PumpToServer();

        Assert.Equal(PlasmaShellRole.Panel, Tracked(fixture).Role);
        AssertClientAlive(host);
    }

    [Theory]
    [InlineData(Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Desktop, PlasmaShellRole.Desktop)]
    [InlineData(Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Panel, PlasmaShellRole.Panel)]
    [InlineData(Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Onscreendisplay, PlasmaShellRole.OnScreenDisplay)]
    [InlineData(Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Notification, PlasmaShellRole.Notification)]
    [InlineData(Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Tooltip, PlasmaShellRole.Tooltip)]
    [InlineData(Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Criticalnotification, PlasmaShellRole.CriticalNotification)]
    [InlineData(Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Appletpopup, PlasmaShellRole.AppletPopup)]
    public void Each_role_lands_in_its_layer(
        Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role wireRole, PlasmaShellRole expected)
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, plasma) = CreateSurface(host, fixture);

        plasma.SetRole((uint)wireRole);
        CommitBuffer(host, surface);

        var tracked = Tracked(fixture);
        Assert.Equal(expected, tracked.Role);
        var scene = fixture.Placement.SceneOf(tracked);
        Assert.NotNull(scene);
        Assert.Same(fixture.Placement.TreeOf(expected), scene!.Tree.Parent);
    }

    [Theory]
    [InlineData(Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Onscreendisplay)]
    [InlineData(Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Tooltip)]
    public void Osd_and_tooltip_stay_unfocusable(Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role wireRole)
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, plasma) = CreateSurface(host, fixture);

        plasma.SetRole((uint)wireRole);
        CommitBuffer(host, surface);
        plasma.SetPanelTakesFocus(1);
        host.PumpToServer();

        var tracked = Tracked(fixture);
        Assert.True(tracked.TakesFocus);
        Assert.False(tracked.Focusable);
        Assert.Null(host.Seat.Keyboard.Focus);
    }

    [Fact]
    public void A_position_before_the_first_commit_applies_at_the_first_commit()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, plasma) = CreateSurface(host, fixture);

        plasma.SetRole((uint)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Panel);
        plasma.SetPosition(12, 90);
        host.PumpToServer();
        Assert.Null(fixture.Placement.SceneOf(Tracked(fixture)));

        CommitBuffer(host, surface);
        var scene = fixture.Placement.SceneOf(Tracked(fixture));
        Assert.NotNull(scene);
        Assert.Equal((12, 90), (scene!.Tree.X, scene.Tree.Y));
    }

    [Fact]
    public void A_panel_reserves_space_and_auto_hide_releases_it()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, plasma) = CreateSurface(host, fixture);
        var output = host.Output;
        var full = host.Layout.BoxOf(output);

        plasma.SetRole((uint)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Panel);
        plasma.SetPosition(0, full.Height - 20);
        CommitBuffer(host, surface, full.Width, 20);

        var usable = fixture.Placement.UsableArea(output);
        Assert.Equal(full.Height - 20, usable.Height);

        plasma.PanelAutoHideHide();
        host.PumpToServer();
        Assert.Equal(full, fixture.Placement.UsableArea(output));

        plasma.PanelAutoHideShow();
        host.PumpToServer();
        Assert.Equal(full.Height - 20, fixture.Placement.UsableArea(output).Height);
    }

    [Fact]
    public void Auto_hide_answers_and_leaves_the_surface_mapped()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, plasma) = CreateSurface(host, fixture);

        var hidden = 0;
        var shown = 0;
        plasma.AutoHiddenPanelHidden += (_, _) => hidden++;
        plasma.AutoHiddenPanelShown += (_, _) => shown++;

        plasma.SetRole((uint)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Panel);
        plasma.SetPosition(0, 100);
        CommitBuffer(host, surface, 160, 20);

        plasma.PanelAutoHideHide();
        host.PumpToServer();
        host.PumpToClient();

        var tracked = Tracked(fixture);
        Assert.Equal(1, hidden);
        Assert.True(tracked.IsAutoHidden);
        Assert.True(tracked.Surface.IsMapped);
        Assert.False(fixture.Placement.SceneOf(tracked)!.Tree.Enabled);

        plasma.PanelAutoHideShow();
        host.PumpToServer();
        host.PumpToClient();
        Assert.Equal(1, shown);
        Assert.True(fixture.Placement.SceneOf(tracked)!.Tree.Enabled);
    }

    [Fact]
    public void Auto_hide_show_on_a_non_panel_raises_the_error()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, plasma) = CreateSurface(host, fixture);

        plasma.SetRole((uint)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Desktop);
        plasma.PanelAutoHideShow();
        host.PumpToServer();

        var error = ExpectError(host);
        Assert.Equal((int)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Error.PanelNotAutoHide, error.ErrorCode);
        Assert.Equal("org_kde_plasma_surface", error.InterfaceName);
        host.DisconnectClient(host.Client);
    }

    [Fact]
    public void Open_under_cursor_places_at_the_pointer_and_clamps()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        host.Seat.Pointer.SendMotion(0, 150, 110);
        var (surface, plasma) = CreateSurface(host, fixture);

        plasma.SetRole((uint)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Appletpopup);
        plasma.OpenUnderCursor();
        CommitBuffer(host, surface, 40, 30);

        var scene = fixture.Placement.SceneOf(Tracked(fixture));
        Assert.NotNull(scene);
        Assert.Equal((120, 90), (scene!.Tree.X, scene.Tree.Y));
    }

    [Fact]
    public void Open_under_cursor_with_no_pointer_places_normally()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, withSeat: false);
        var (surface, plasma) = CreateSurface(host, fixture);

        plasma.SetRole((uint)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Appletpopup);
        plasma.OpenUnderCursor();
        CommitBuffer(host, surface, 40, 30);

        var scene = fixture.Placement.SceneOf(Tracked(fixture));
        Assert.NotNull(scene);
        Assert.Equal((60, 45), (scene!.Tree.X, scene.Tree.Y));
        AssertClientAlive(host);
    }

    [Fact]
    public void Skip_taskbar_reaches_the_toplevel_state_and_the_plasma_window()
    {
        using var host = new CompositorTestHost();
        using var toplevels = new XdgToplevelSource(host.Shell);
        var model = new Basin.Capabilities.AggregateToplevelModel();
        model.Add(toplevels);
        using var windowManager = new Basin.Desktop.PlasmaWindowManager(host.Display, model, null);
        using var fixture = Start(host, toplevels: toplevels);

        var window = MappedToplevel.Map(host, host.Client);
        var plasma = fixture.Proxy.GetSurface(window.Surface);
        host.PumpToServer();

        var states = new List<uint>();
        Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement? management = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_plasma_window_management")
            {
                management = registry.Bind<Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement>(e.Name, 20);
                management.WindowWithUuid += (_, we) =>
                {
                    var proxy = management.GetWindowByUuid(we.Uuid);
                    proxy.StateChanged += (_, se) => states.Add(se.Flags);
                };
            }
        };
        host.PumpToClient();
        Assert.NotNull(management);
        host.PumpToServer();
        host.PumpToClient();

        plasma.SetSkipTaskbar(1);
        plasma.SetSkipSwitcher(1);
        host.PumpToServer();
        host.PumpToClient();

        var id = toplevels.IdFor(window.ServerToplevel);
        Assert.True(toplevels.TryGet(id, out var info));
        Assert.True(info.State.HasFlag(Basin.Capabilities.ToplevelState.SkipTaskbar));
        Assert.True(info.State.HasFlag(Basin.Capabilities.ToplevelState.SkipSwitcher));
        Assert.Contains(states, flags => (flags & 0x1000) != 0);
        Assert.Contains(states, flags => (flags & 0x40000) != 0);
    }

    [Fact]
    public void Destroying_the_plasma_surface_unmaps_it()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, plasma) = CreateSurface(host, fixture);

        plasma.SetRole((uint)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Notification);
        CommitBuffer(host, surface);
        var tracked = Tracked(fixture);
        var scene = fixture.Placement.SceneOf(tracked);
        Assert.NotNull(scene);

        plasma.Destroy();
        host.PumpToServer();

        Assert.True(tracked.IsDestroyed);
        Assert.True(scene!.IsDestroyed);
        Assert.Empty(fixture.Manager.Surfaces);
        AssertClientAlive(host);
    }

    private static WaylandProtocolException ExpectError(CompositorTestHost host)
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                host.PumpToClient();
                host.PumpToServer();
            }
            catch (WaylandProtocolException error)
            {
                return error;
            }
        }

        throw new TimeoutException("no protocol error arrived while pumping");
    }

    private static void AssertClientAlive(CompositorTestHost host)
    {
        var sync = host.Client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.True(done);
    }
}
