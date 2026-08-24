using Basin.Plasma;
using Basin.Scene;
using Basin.Seat;
using Basin.Shell.Xdg;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ScreenEdgeTests
{
    private const uint BtnLeft = 0x110;

    private sealed class EdgeFixture : IDisposable
    {
        public required LayerShell LayerShell;
        public required ScreenEdgeManager Manager;
        public required PlasmaScreenEdges Edges;
        public required Basin.Plasma.Protocol.KdeScreenEdgeManagerV1 Proxy;
        public LayerSurface? ServerLayer;

        public void Dispose()
        {
            Manager.Dispose();
            Edges.Dispose();
            LayerShell.Dispose();
        }
    }

    private static EdgeFixture Start(
        CompositorTestHost host, bool withScene = true, bool withSeat = true, bool withEdges = true)
    {
        var layerShell = new LayerShell(host.Display, host.Compositor);
        var edges = new PlasmaScreenEdges(host.Loop, withSeat ? host.Seat : null, host.Layout);
        var manager = new ScreenEdgeManager(
            host.Display,
            host.Compositor,
            withScene ? host.Scene : null,
            host.Layout,
            withEdges ? edges : null);
        var fixture = new EdgeFixture
        {
            LayerShell = layerShell,
            Manager = manager,
            Edges = edges,
            Proxy = null!,
        };
        layerShell.NewSurface += layer =>
        {
            fixture.ServerLayer = layer;
            layer.InitialCommit += () => layer.Configure(160, 20);
        };

        Basin.Plasma.Protocol.KdeScreenEdgeManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "kde_screen_edge_manager_v1")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.KdeScreenEdgeManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        fixture.Proxy = proxy!;
        return fixture;
    }

    private static (WlSurface Surface, LayerSurface Server) MapPanel(
        CompositorTestHost host, EdgeFixture fixture, int zone = 20)
    {
        var client = host.Client;
        var shellProxy = BindLayerShell(host, client);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(
            surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetAnchor(
            Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Bottom |
            Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Left |
            Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Right);
        layerProxy.SetSize(0, 20);
        layerProxy.SetExclusiveZone(zone);
        layerProxy.Configure += (_, e) => layerProxy.AckConfigure(e.Serial);
        surface.Commit();
        host.PumpUntil(() => fixture.ServerLayer is not null);

        var buffer = client.CreateBuffer(160, 20, Fill.Solid(160, 20, 0xFF285577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 160, 20);
        surface.Commit();
        host.PumpUntil(() => fixture.ServerLayer!.IsMapped);
        return (surface, fixture.ServerLayer!);
    }

    private static Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1 BindLayerShell(
        CompositorTestHost host, ShmTestClient client)
    {
        Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1? shell = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_layer_shell_v1")
            {
                shell = registry.Bind<Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1>(e.Name, 4);
            }
        };
        host.PumpToClient();
        Assert.NotNull(shell);
        return shell!;
    }

    private static SceneSurface SceneOf(CompositorTestHost host, LayerSurface layer)
    {
        foreach (var scene in host.SurfaceScenes)
        {
            if (ReferenceEquals(scene.Surface, layer.Surface))
            {
                return scene;
            }
        }

        throw new InvalidOperationException("no scene node for the layer surface");
    }

    private static Basin.Plasma.Protocol.KdeAutoHideScreenEdgeV1 BottomEdge(
        EdgeFixture fixture, WlSurface surface) =>
        fixture.Proxy.GetAutoHideScreenEdge(
            Basin.Plasma.Protocol.KdeScreenEdgeManagerV1.Border.Bottom, surface);

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

    [Theory]
    [InlineData(0u)]
    [InlineData(5u)]
    public void A_border_outside_the_enum_raises_invalid_border(uint border)
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, _) = MapPanel(host, fixture);

        fixture.Proxy.GetAutoHideScreenEdge(
            (Basin.Plasma.Protocol.KdeScreenEdgeManagerV1.Border)border, surface);
        var error = ExpectError(host);

        Assert.Equal(
            (int)Basin.Plasma.Protocol.KdeScreenEdgeManagerV1.Error.InvalidBorder, error.ErrorCode);
        Assert.Equal("kde_screen_edge_manager_v1", error.InterfaceName);
        host.DisconnectClient(host.Client);
    }

    [Fact]
    public void A_toplevel_surface_raises_invalid_role()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var window = MappedToplevel.Map(host, host.Client);

        BottomEdge(fixture, window.Surface);
        var error = ExpectError(host);

        Assert.Equal(
            (int)Basin.Plasma.Protocol.KdeScreenEdgeManagerV1.Error.InvalidRole, error.ErrorCode);
        host.DisconnectClient(host.Client);
    }

    [Fact]
    public void A_surface_with_no_role_raises_invalid_role()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();

        BottomEdge(fixture, surface);
        var error = ExpectError(host);

        Assert.Equal(
            (int)Basin.Plasma.Protocol.KdeScreenEdgeManagerV1.Error.InvalidRole, error.ErrorCode);
        host.DisconnectClient(host.Client);
    }

    [Fact]
    public void A_second_edge_on_the_same_surface_raises_already_constructed()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, _) = MapPanel(host, fixture);

        BottomEdge(fixture, surface);
        host.PumpToServer();
        BottomEdge(fixture, surface);
        var error = ExpectError(host);

        Assert.Equal(
            (int)Basin.Plasma.Protocol.KdeScreenEdgeManagerV1.Error.AlreadyConstructed,
            error.ErrorCode);
        host.DisconnectClient(host.Client);
    }

    [Fact]
    public void Activate_hides_the_node_keeps_the_surface_mapped_and_releases_the_zone()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);
        var full = host.Layout.BoxOf(host.Output);

        var (_, reserved) = ArrangeOnce(full, server, scene);
        Assert.Equal(full.Height - 20, reserved.Height);

        var edge = BottomEdge(fixture, surface);
        edge.Activate();
        host.PumpToServer();

        Assert.False(scene.Tree.Enabled);
        Assert.True(server.IsMapped);
        Assert.True(fixture.Manager.For(server.Surface)!.IsHidden);

        var (_, released) = ArrangeOnce(full, server, scene);
        Assert.Equal(full, released);
    }

    [Fact]
    public void A_trigger_reveals_and_disarms_until_the_next_activate()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);

        var edge = BottomEdge(fixture, surface);
        edge.Activate();
        host.PumpToServer();
        Assert.False(scene.Tree.Enabled);
        Assert.True(fixture.Manager.For(server.Surface)!.IsArmed);

        host.Seat.Pointer.SendMotion(0, 80, 119);
        host.Seat.Pointer.NotifyButton(0, BtnLeft, pressed: true);
        host.Seat.Pointer.NotifyButton(0, BtnLeft, pressed: false);

        Assert.True(scene.Tree.Enabled);
        Assert.False(fixture.Manager.For(server.Surface)!.IsHidden);
        Assert.False(fixture.Manager.For(server.Surface)!.IsArmed);

        host.Seat.Pointer.NotifyButton(0, BtnLeft, pressed: true);
        host.Seat.Pointer.NotifyButton(0, BtnLeft, pressed: false);
        Assert.True(scene.Tree.Enabled);
        Assert.False(fixture.Manager.For(server.Surface)!.IsHidden);

        edge.Activate();
        host.PumpToServer();
        Assert.False(scene.Tree.Enabled);
        Assert.True(fixture.Manager.For(server.Surface)!.IsArmed);
    }

    [Fact]
    public void Deactivate_reveals_without_a_trigger()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);

        var edge = BottomEdge(fixture, surface);
        edge.Activate();
        host.PumpToServer();
        Assert.False(scene.Tree.Enabled);

        edge.Deactivate();
        host.PumpToServer();

        Assert.True(scene.Tree.Enabled);
        Assert.False(fixture.Manager.For(server.Surface)!.IsArmed);
    }

    [Fact]
    public void Deactivate_on_an_inactive_edge_is_not_an_error()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);

        var edge = BottomEdge(fixture, surface);
        edge.Deactivate();
        host.PumpToServer();

        Assert.True(scene.Tree.Enabled);
        AssertClientAlive(host);
    }

    [Fact]
    public void Destroying_an_active_edge_reveals_the_surface()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);

        var edge = BottomEdge(fixture, surface);
        edge.Activate();
        host.PumpToServer();
        Assert.False(scene.Tree.Enabled);

        edge.Destroy();
        host.PumpToServer();

        Assert.True(scene.Tree.Enabled);
        Assert.Null(fixture.Manager.For(server.Surface));
        AssertClientAlive(host);
    }

    [Fact]
    public void Destroying_the_manager_leaves_existing_edges_working()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);

        var edge = BottomEdge(fixture, surface);
        host.PumpToServer();
        fixture.Proxy.Destroy();
        host.PumpToServer();

        edge.Activate();
        host.PumpToServer();
        Assert.False(scene.Tree.Enabled);

        edge.Deactivate();
        host.PumpToServer();
        Assert.True(scene.Tree.Enabled);
        AssertClientAlive(host);
    }

    [Fact]
    public void A_pointer_at_the_border_of_another_output_does_not_trigger()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000));
        host.Layout.Add(second, 160, 0);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);

        var edge = BottomEdge(fixture, surface);
        edge.Activate();
        host.PumpToServer();
        Assert.False(scene.Tree.Enabled);

        host.Seat.Pointer.SendMotion(0, 240, 119);
        host.Seat.Pointer.NotifyButton(0, BtnLeft, pressed: true);
        host.Seat.Pointer.NotifyButton(0, BtnLeft, pressed: false);
        Assert.False(scene.Tree.Enabled);

        host.Seat.Pointer.SendMotion(0, 80, 119);
        host.Seat.Pointer.NotifyButton(0, BtnLeft, pressed: true);
        Assert.True(scene.Tree.Enabled);
    }

    [Fact]
    public void A_brief_crossing_does_not_trigger_and_a_press_does()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);

        var edge = BottomEdge(fixture, surface);
        edge.Activate();
        host.PumpToServer();
        Assert.False(scene.Tree.Enabled);

        host.Seat.Pointer.SendMotion(0, 80, 119);
        host.Seat.Pointer.SendMotion(0, 80, 60);
        host.PumpToServer();
        Assert.False(scene.Tree.Enabled);

        host.Seat.Pointer.SendMotion(0, 80, 119);
        host.Seat.Pointer.NotifyButton(0, BtnLeft, pressed: true);
        Assert.True(scene.Tree.Enabled);
    }

    [Fact]
    public void Activate_before_the_panel_maps_hides_it_at_map()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var client = host.Client;
        var shellProxy = BindLayerShell(host, client);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(
            surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetAnchor(Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Bottom);
        layerProxy.SetSize(0, 20);
        layerProxy.Configure += (_, e) => layerProxy.AckConfigure(e.Serial);
        surface.Commit();
        host.PumpUntil(() => fixture.ServerLayer is not null);
        var server = fixture.ServerLayer!;

        SceneOf(host, server).Destroy();
        SceneSurface? late = null;
        server.Mapped += () => late = new SceneSurface(host.Scene.Root, server.Surface);

        var edge = BottomEdge(fixture, surface);
        edge.Activate();
        host.PumpToServer();
        Assert.True(fixture.Manager.For(server.Surface)!.IsHidden);

        var buffer = client.CreateBuffer(160, 20, Fill.Solid(160, 20, 0xFF285577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 160, 20);
        surface.Commit();
        host.PumpUntil(() => server.IsMapped);

        Assert.NotNull(late);
        Assert.False(late!.Tree.Enabled);
    }

    [Fact]
    public void A_press_over_a_surface_away_from_the_origin_still_triggers()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);
        scene.Tree.SetPosition(0, 100);

        var edge = BottomEdge(fixture, surface);
        edge.Activate();
        host.PumpToServer();
        Assert.False(scene.Tree.Enabled);

        host.Seat.Pointer.NotifyMotionAt(0, server.Surface, 80, 19, 80, 119);
        host.Seat.Pointer.NotifyButton(0, BtnLeft, pressed: true);

        Assert.True(scene.Tree.Enabled);
        Assert.False(fixture.Manager.For(server.Surface)!.IsHidden);
    }

    [Fact]
    public void A_touch_edge_swipe_from_the_armed_border_triggers()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);
        var router = new TouchRouter(host.Seat.Touch) { Gestures = fixture.Edges.TouchGesture };

        var edge = BottomEdge(fixture, surface);
        edge.Activate();
        host.PumpToServer();
        Assert.False(scene.Tree.Enabled);

        router.Down(1, 0, 80, 118);
        router.Motion(20, 0, 80, 100);
        router.Motion(40, 0, 80, 80);
        router.Up(60, 0);

        Assert.True(scene.Tree.Enabled);
        Assert.False(fixture.Manager.For(server.Surface)!.IsArmed);
    }

    [Fact]
    public void A_touch_swipe_with_nothing_armed_passes_through()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        MapPanel(host, fixture);
        var router = new TouchRouter(host.Seat.Touch) { Gestures = fixture.Edges.TouchGesture };

        router.Down(1, 0, 80, 118);
        Assert.False(fixture.Edges.TouchGesture.IsActive);
        router.Up(20, 0);
        AssertClientAlive(host);
    }

    [Fact]
    public void Without_a_scene_or_seat_the_edge_degrades_and_the_client_survives()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host, withScene: false, withSeat: false, withEdges: false);
        var (surface, server) = MapPanel(host, fixture);
        var scene = SceneOf(host, server);

        var edge = BottomEdge(fixture, surface);
        edge.Activate();
        host.PumpToServer();

        Assert.True(scene.Tree.Enabled);
        Assert.True(fixture.Manager.For(server.Surface)!.IsHidden);
        Assert.False(fixture.Manager.For(server.Surface)!.IsArmed);

        edge.Deactivate();
        host.PumpToServer();
        Assert.False(fixture.Manager.For(server.Surface)!.IsHidden);

        edge.Destroy();
        host.PumpToServer();
        Assert.Null(fixture.Manager.For(server.Surface));
        AssertClientAlive(host);
    }

    private static (LayerSurface Layer, Box Usable) ArrangeOnce(
        Box full, LayerSurface server, SceneSurface scene)
    {
        var usable = Basin.Desktop.LayerArrangement.Arrange(full, [(server, scene)]);
        return (server, usable);
    }
}
