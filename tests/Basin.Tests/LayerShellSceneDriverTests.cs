using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class LayerShellSceneDriverTests
{
    [Fact]
    public void A_panel_maps_into_the_top_tree_and_claims_its_zone()
    {
        using var host = new CompositorTestHost();
        var layerShell = new LayerShell(host.Display, host.Compositor);
        var layers = new SceneLayers(host.Scene.Root);
        var driver = new LayerShellSceneDriver(layerShell, host.Layout, layers);
        var usableByOutput = new Dictionary<Basin.IOutput, Basin.Box>();
        driver.UsableAreaChanged += (output, usable) => usableByOutput[output] = usable;
        SceneSurface? created = null;
        driver.SceneCreated += (_, scene) => created = scene;

        var client = host.Client;
        var shellProxy = BindLayerShell(host, client);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(
            surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetAnchor(
            Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Top |
            Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Left |
            Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Right);
        layerProxy.SetSize(0, 30);
        layerProxy.SetExclusiveZone(30);
        var configured = (Serial: 0u, Width: 0);
        layerProxy.Configure += (_, e) =>
        {
            configured = (e.Serial, (int)e.Width);
            layerProxy.AckConfigure(e.Serial);
        };
        surface.Commit();
        host.PumpUntil(() => configured.Serial != 0);

        var buffer = client.CreateBuffer(configured.Width, 30, Fill.Solid(configured.Width, 30, 0xFF285577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, configured.Width, 30);
        surface.Commit();
        host.PumpUntil(() => created is not null);

        Assert.Same(layers.Top, created!.Tree.Parent);
        Assert.Single(driver.Surfaces);
        Assert.Equal(30, usableByOutput[host.Output].Y);

        var removed = false;
        driver.Removed += _ => removed = true;
        surface.Attach(null, 0, 0);
        surface.Commit();
        host.PumpUntil(() => removed);
        Assert.Empty(driver.Surfaces);
        Assert.True(created.IsDestroyed);
        Assert.Equal(0, usableByOutput[host.Output].Y);
        layerShell.Dispose();
    }

    [Fact]
    public void A_refused_surface_is_closed_not_killed()
    {
        using var host = new CompositorTestHost();
        var layerShell = new LayerShell(host.Display, host.Compositor);
        var layers = new SceneLayers(host.Scene.Root);
        _ = new LayerShellSceneDriver(layerShell, host.Layout, layers) { Accept = _ => false };

        var client = host.Client;
        var shellProxy = BindLayerShell(host, client);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(
            surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        var closed = false;
        layerProxy.Closed += (_, _) => closed = true;
        surface.Commit();
        host.PumpUntil(() => closed);
        host.PumpToServer();
        Assert.True(closed);
        layerShell.Dispose();
    }

    [Fact]
    public void A_menu_a_layer_surface_roots_lands_in_that_layer_with_its_submenu()
    {
        using var host = new CompositorTestHost();
        var layerShell = new LayerShell(host.Display, host.Compositor);
        var layers = new SceneLayers(host.Scene.Root);
        var driver = new LayerShellSceneDriver(layerShell, host.Layout, layers);
        driver.TrackPopups(host.Shell);
        var popupScenes = new List<SceneSurface>();
        driver.PopupSceneCreated += (_, _, scene) => popupScenes.Add(scene);
        SceneSurface? panel = null;
        driver.SceneCreated += (_, scene) => panel = scene;

        var client = host.Client;
        var shellProxy = BindLayerShell(host, client);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(
            surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetSize(200, 30);
        var acked = 0u;
        layerProxy.Configure += (_, e) =>
        {
            acked = e.Serial;
            layerProxy.AckConfigure(e.Serial);
        };
        surface.Commit();
        host.PumpUntil(() => acked != 0);

        var panelBuffer = client.CreateBuffer(200, 30, Fill.Solid(200, 30, 0xFF285577));
        surface.Attach(panelBuffer.Proxy, 0, 0);
        surface.Damage(0, 0, 200, 30);
        surface.Commit();
        host.PumpUntil(() => panel is not null);

        var menuPositioner = client.WmBase!.CreatePositioner();
        menuPositioner.SetSize(40, 60);
        menuPositioner.SetAnchorRect(10, 10, 1, 1);
        var menuSurface = client.Compositor.CreateSurface();
        var menuXdg = client.WmBase.GetXdgSurface(menuSurface);
        var menuPopup = menuXdg.GetPopup(null, menuPositioner);
        layerProxy.GetPopup(menuPopup);
        menuXdg.Configure += (_, e) => menuXdg.AckConfigure(e.Serial);
        menuSurface.Commit();
        host.PumpUntil(() => popupScenes.Count == 1);

        var subPositioner = client.WmBase.CreatePositioner();
        subPositioner.SetSize(40, 40);
        subPositioner.SetAnchorRect(5, 5, 1, 1);
        var subSurface = client.Compositor.CreateSurface();
        var subXdg = client.WmBase.GetXdgSurface(subSurface);
        var subPopup = subXdg.GetPopup(menuXdg, subPositioner);
        subXdg.Configure += (_, e) => subXdg.AckConfigure(e.Serial);
        subSurface.Commit();
        host.PumpUntil(() => popupScenes.Count == 2);

        Assert.Same(panel!.Tree, popupScenes[0].Tree.Parent);
        Assert.Same(panel.Tree, popupScenes[1].Tree.Parent);

        subPopup.Destroy();
        subXdg.Destroy();
        subSurface.Dispose();
        menuPopup.Destroy();
        menuXdg.Destroy();
        menuSurface.Dispose();
        host.PumpToServer();
        Assert.True(popupScenes[0].IsDestroyed);
        Assert.True(popupScenes[1].IsDestroyed);
        layerShell.Dispose();
    }

    private static Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1 BindLayerShell(
        CompositorTestHost host, ShmTestClient client, uint version = 4)
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
