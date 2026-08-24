using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.Xdg;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class LayerLayoutTests
{
    private static LayerLayout.LayerSpec Spec(LayerKind layer, LayerAnchor anchor, int zone, int width, int height) =>
        new(layer, anchor, zone, (0, 0, 0, 0), width, height);

    [Fact]
    public void Top_bar_reserves_its_exclusive_zone()
    {
        var bar = Spec(LayerKind.Top, LayerAnchor.Top | LayerAnchor.Left | LayerAnchor.Right, 30, 0, 30);
        var (placements, usable) = LayerLayout.Arrange(new Box(0, 0, 1000, 800), [bar]);

        Assert.Equal(new Box(0, 0, 1000, 30), placements[0].Box);
        Assert.Equal(new Box(0, 30, 1000, 770), usable);
    }

    [Fact]
    public void Side_dock_and_bottom_bar_stack_their_zones()
    {
        var (placements, usable) = LayerLayout.Arrange(
            new Box(0, 0, 1000, 800),
            [
                Spec(LayerKind.Top, LayerAnchor.Left, 40, 40, 200),
                Spec(LayerKind.Bottom, LayerAnchor.Bottom | LayerAnchor.Left | LayerAnchor.Right, 24, 0, 24),
            ]);

        Assert.Equal(new Box(0, 300, 40, 200), placements.Single(p => p.Index == 0).Box);
        Assert.Equal(new Box(40, 776, 960, 24), placements.Single(p => p.Index == 1).Box);
        Assert.Equal(new Box(40, 0, 960, 776), usable);
    }

    [Fact]
    public void Unanchored_negative_zone_surface_centers_in_the_full_output()
    {
        var (placements, _) = LayerLayout.Arrange(
            new Box(0, 0, 1000, 800),
            [
                Spec(LayerKind.Top, LayerAnchor.Top | LayerAnchor.Left | LayerAnchor.Right, 100, 0, 100),
                Spec(LayerKind.Overlay, LayerAnchor.None, -1, 200, 100),
            ]);

        var box = placements.Single(p => p.Index == 1).Box;
        Assert.Equal(new Box(400, 350, 200, 100), box);
    }

    [Fact]
    public void A_negative_zone_wallpaper_spans_the_full_output_beside_a_bar()
    {
        var all = LayerAnchor.Top | LayerAnchor.Bottom | LayerAnchor.Left | LayerAnchor.Right;
        var (placements, usable) = LayerLayout.Arrange(
            new Box(0, 0, 1000, 800),
            [
                Spec(LayerKind.Bottom, LayerAnchor.Bottom | LayerAnchor.Left | LayerAnchor.Right, 56, 0, 56),
                Spec(LayerKind.Background, all, -1, 0, 0),
            ]);

        Assert.Equal(new Box(0, 0, 1000, 800), placements.Single(p => p.Index == 1).Box);
        Assert.Equal(new Box(0, 0, 1000, 744), usable);
    }

    [Fact]
    public void A_zero_zone_surface_keeps_out_of_exclusive_zones()
    {
        var all = LayerAnchor.Top | LayerAnchor.Bottom | LayerAnchor.Left | LayerAnchor.Right;
        var (placements, _) = LayerLayout.Arrange(
            new Box(0, 0, 1000, 800),
            [
                Spec(LayerKind.Top, LayerAnchor.Top | LayerAnchor.Left | LayerAnchor.Right, 30, 0, 30),
                Spec(LayerKind.Bottom, all, 0, 0, 0),
            ]);

        Assert.Equal(new Box(0, 30, 1000, 770), placements.Single(p => p.Index == 1).Box);
    }

    [Fact]
    public void A_corner_anchored_panel_claims_nothing_until_an_exclusive_edge_names_one()
    {
        var corner = Spec(LayerKind.Top, LayerAnchor.Top | LayerAnchor.Left, 30, 200, 30);
        var (_, ambiguous) = LayerLayout.Arrange(new Box(0, 0, 1000, 800), [corner]);
        Assert.Equal(new Box(0, 0, 1000, 800), ambiguous);

        var (_, resolved) = LayerLayout.Arrange(
            new Box(0, 0, 1000, 800),
            [corner with { ExclusiveEdge = LayerAnchor.Top }]);
        Assert.Equal(new Box(0, 30, 1000, 770), resolved);

        var (_, sideways) = LayerLayout.Arrange(
            new Box(0, 0, 1000, 800),
            [corner with { ExclusiveEdge = LayerAnchor.Left }]);
        Assert.Equal(new Box(30, 0, 970, 800), sideways);
    }
}

public sealed class LayerShellProtocolTests
{
    [Fact]
    public void Panel_lifecycle_configure_map_and_exclusive_zone()
    {
        using var host = new CompositorTestHost();
        var layerShell = new LayerShell(host.Display, host.Compositor);
        LayerSurface? serverLayer = null;
        layerShell.NewSurface += layer =>
        {
            serverLayer = layer;
            layer.InitialCommit += () => layer.Configure(160, 30);
        };

        var client = host.Client;
        var shellProxy = BindLayerShell(host, client);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetAnchor(Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Top | Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Left | Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Right);
        layerProxy.SetSize(0, 30);
        layerProxy.SetExclusiveZone(30);

        uint configureSerial = 0;
        var configuredWidth = 0;
        layerProxy.Configure += (_, e) =>
        {
            configureSerial = e.Serial;
            configuredWidth = (int)e.Width;
            layerProxy.AckConfigure(e.Serial);
        };

        surface.Commit();
        host.PumpUntil(() => configureSerial != 0);
        Assert.Equal(160, configuredWidth);
        Assert.NotNull(serverLayer);
        Assert.Equal(30, serverLayer!.ExclusiveZone);
        Assert.Equal(LayerAnchor.Top | LayerAnchor.Left | LayerAnchor.Right, serverLayer.Anchor);

        var mapped = false;
        serverLayer.Mapped += () => mapped = true;
        var buffer = client.CreateBuffer(160, 30, Fill.Solid(160, 30, 0xFF285577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 160, 30);
        surface.Commit();
        host.PumpUntil(() => mapped);
        Assert.True(serverLayer.IsMapped);

        surface.Attach(null, 0, 0);
        surface.Commit();
        host.PumpUntil(() => !serverLayer.IsMapped);

        surface.Dispose();
        host.PumpToServer();
        layerShell.Dispose();
    }

    [Fact]
    public void A_layer_surface_destroyed_before_mapping_raises_Destroyed()
    {
        using var host = new CompositorTestHost();
        var layerShell = new LayerShell(host.Display, host.Compositor);
        LayerSurface? serverLayer = null;
        layerShell.NewSurface += layer =>
        {
            serverLayer = layer;
            layer.InitialCommit += () => layer.Configure(160, 40);
        };

        var client = host.Client;
        var shellProxy = BindLayerShell(host, client);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "dock");
        layerProxy.SetSize(0, 40);
        layerProxy.SetExclusiveZone(40);
        surface.Commit();
        host.PumpUntil(() => serverLayer is not null);

        var destroyed = false;
        var unmapped = false;
        serverLayer!.Destroyed += () => destroyed = true;
        serverLayer.Unmapped += () => unmapped = true;

        layerProxy.Destroy();
        host.PumpToServer();

        Assert.True(destroyed);
        Assert.False(unmapped);
        Assert.False(serverLayer.IsMapped);
        Assert.True(serverLayer.IsDestroyed);

        surface.Dispose();
        host.PumpToServer();
        layerShell.Dispose();
    }

    [Fact]
    public void A_destroyed_layer_surface_frees_the_role_for_a_new_one()
    {
        using var host = new CompositorTestHost();
        var layerShell = new LayerShell(host.Display, host.Compositor);
        var serverLayers = new List<LayerSurface>();
        layerShell.NewSurface += layer =>
        {
            serverLayers.Add(layer);
            layer.InitialCommit += () => layer.Configure(160, 30);
        };

        var client = host.Client;
        var shellProxy = BindLayerShell(host, client);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetSize(0, 30);
        layerProxy.Configure += (_, e) => layerProxy.AckConfigure(e.Serial);
        surface.Commit();
        host.PumpUntil(() => serverLayers.Count == 1);

        layerProxy.Destroy();
        surface.Attach(null, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var secondProxy = shellProxy.GetLayerSurface(surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        secondProxy.SetSize(0, 40);
        uint secondSerial = 0;
        secondProxy.Configure += (_, e) =>
        {
            secondSerial = e.Serial;
            secondProxy.AckConfigure(e.Serial);
        };
        surface.Commit();
        host.PumpUntil(() => secondSerial != 0);

        Assert.Equal(2, serverLayers.Count);
        var mapped = false;
        serverLayers[1].Mapped += () => mapped = true;
        var buffer = client.CreateBuffer(160, 40, Fill.Solid(160, 40, 0xFF285577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpUntil(() => mapped);
        Assert.True(serverLayers[1].IsMapped);
        Assert.False(serverLayers[0].IsMapped);

        surface.Dispose();
        host.PumpToServer();
        layerShell.Dispose();
    }

    [Fact]
    public void Adopted_popup_anchors_to_its_layer_surface()
    {
        using var host = new CompositorTestHost();
        var layerShell = new LayerShell(host.Display, host.Compositor);
        LayerSurface? serverLayer = null;
        layerShell.NewSurface += layer =>
        {
            serverLayer = layer;
            layer.InitialCommit += () => layer.Configure(160, 30);
        };

        var client = host.Client;
        var shellProxy = BindLayerShell(host, client);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetSize(0, 30);
        layerProxy.Configure += (_, e) => layerProxy.AckConfigure(e.Serial);
        surface.Commit();
        host.PumpUntil(() => serverLayer is not null);

        XdgPopupWindow? adopted = null;
        serverLayer!.PopupAdopted += popup => adopted = popup;

        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(50, 40);
        positioner.SetAnchorRect(0, 0, 20, 20);
        var popupSurface = client.Compositor.CreateSurface();
        var popupXdg = client.WmBase.GetXdgSurface(popupSurface);
        var popup = popupXdg.GetPopup(null, positioner);
        layerProxy.GetPopup(popup);
        popupXdg.Configure += (_, e) => popupXdg.AckConfigure(e.Serial);
        popupSurface.Commit();
        host.PumpUntil(() => adopted is not null);

        Assert.Null(adopted!.Parent);
        Assert.Same(serverLayer, adopted.LayerParent);

        surface.Dispose();
        host.PumpToServer();
        layerShell.Dispose();
    }

    [Fact]
    public void Exclusive_edge_latches_on_commit_and_rejects_an_unanchored_edge()
    {
        using var host = new CompositorTestHost();
        var layerShell = new LayerShell(host.Display, host.Compositor);
        LayerSurface? serverLayer = null;
        layerShell.NewSurface += layer =>
        {
            serverLayer = layer;
            layer.InitialCommit += () => layer.Configure(200, 30);
        };

        var client = host.Client;
        var shellProxy = BindLayerShell(host, client, version: 5);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "corner");
        layerProxy.SetAnchor(Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Top | Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Left);
        layerProxy.SetSize(200, 30);
        layerProxy.SetExclusiveZone(30);
        layerProxy.SetExclusiveEdge(Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Top);
        layerProxy.Configure += (_, e) => layerProxy.AckConfigure(e.Serial);
        surface.Commit();
        host.PumpUntil(() => serverLayer is not null);
        Assert.Equal(LayerAnchor.Top, serverLayer!.ExclusiveEdge);

        var otherSurface = client.Compositor.CreateSurface();
        var otherLayer = shellProxy.GetLayerSurface(otherSurface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "bad");
        otherLayer.SetAnchor(Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Top);
        otherLayer.SetExclusiveEdge(Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Bottom);
        otherSurface.Commit();
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("zwlr_layer_surface_v1", error.Message, StringComparison.Ordinal);
        layerShell.Dispose();
    }

    private static Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1 BindLayerShell(CompositorTestHost host, ShmTestClient client, uint version = 4)
    {
        Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1? shell = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_layer_shell_v1")
            {
                shell = registry.Bind<Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(shell);
        return shell!;
    }
}

public sealed class SessionLockTests
{
    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    public void Lock_surface_maps_and_crash_stays_locked(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var manager = new SessionLockManager(host.Display, host.Compositor);
        var events = new List<string>();
        manager.Locked += () => events.Add("locked");
        manager.Unlocked += () => events.Add("unlocked");
        manager.Abandoned += () => events.Add("abandoned");
        LockSurface? serverSurface = null;
        manager.NewLockSurface += s => serverSurface = s;

        var locker = host.ConnectClient();
        var managerProxy = BindLock(host, locker);
        var lockProxy = managerProxy.Lock();
        var gotLocked = false;
        lockProxy.Locked += (_, _) => gotLocked = true;
        host.PumpUntil(() => gotLocked);
        Assert.True(manager.IsLocked);
        Assert.Equal(["locked"], events);

        var surface = locker.Compositor.CreateSurface();
        var lockSurfaceProxy = lockProxy.GetLockSurface(surface, locker.Outputs[0]);
        var size = (W: 0, H: 0);
        lockSurfaceProxy.Configure += (_, e) =>
        {
            size = ((int)e.Width, (int)e.Height);
            lockSurfaceProxy.AckConfigure(e.Serial);
        };
        host.PumpUntil(() => size.W != 0);
        Assert.Equal((160, 120), size);

        var buffer = locker.CreateBuffer(160, 120, Fill.Solid(160, 120, 0xFF335577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 160, 120);
        surface.Commit();
        host.PumpUntil(() => serverSurface is { IsMapped: true });

        using var sceneSurface = TrackScene(host, serverSurface!);
        host.RenderFrame();
        Assert.Equal(0xFF335577u, host.Pixel(80, 60));

        host.DisconnectClient(locker);
        host.PumpToServer();
        host.PumpToServer();
        Assert.True(manager.IsLocked);
        Assert.Contains("abandoned", events);

        var locker2 = host.ConnectClient();
        var manager2 = BindLock(host, locker2);
        var lock2 = manager2.Lock();
        var gotLocked2 = false;
        lock2.Locked += (_, _) => gotLocked2 = true;
        host.PumpUntil(() => gotLocked2);
        lock2.UnlockAndDestroy();
        host.PumpToServer();
        Assert.False(manager.IsLocked);
        Assert.Contains("unlocked", events);
        manager.Dispose();
    }

    private static IDisposable TrackScene(CompositorTestHost host, LockSurface serverSurface)
    {
        return new Noop();
    }

    private sealed class Noop : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private static Basin.Desktop.Protocol.ExtSessionLockManagerV1 BindLock(CompositorTestHost host, ShmTestClient client)
    {
        Basin.Desktop.Protocol.ExtSessionLockManagerV1? manager = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_session_lock_manager_v1")
            {
                manager = registry.Bind<Basin.Desktop.Protocol.ExtSessionLockManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(manager);
        return manager!;
    }
}

public sealed class LockSceneStructureTests
{
    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    public void Lock_tree_draws_with_disabled_siblings(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var trees = new SceneTree[6];
        for (var i = 0; i < 6; i++)
        {
            trees[i] = new SceneTree(host.Scene.Root);
        }

        for (var i = 0; i < 5; i++)
        {
            trees[i].Enabled = false;
        }

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        host.PumpToServer();
        var serverSurface = host.Compositor.Surfaces.Single();

        host.SurfaceScenes[0].Destroy();
        var scene = new SceneSurface(trees[5], serverSurface);

        var buffer = client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0xFF335577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 160, 120);
        surface.Commit();
        host.PumpToServer();

        host.RenderFrame();
        Assert.Equal(0xFF335577u, host.Pixel(80, 60));

        scene.Destroy();
        surface.Dispose();
        host.PumpToServer();
    }
}
