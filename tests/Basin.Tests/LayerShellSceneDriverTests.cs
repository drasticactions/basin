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

    private static (Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1 Proxy, Func<(int Width, int Height)> Size) MapLayer(
        CompositorTestHost host,
        Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1 shellProxy,
        Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer layer,
        Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor anchor,
        int height,
        int exclusive)
    {
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var proxy = shellProxy.GetLayerSurface(surface, client.Outputs[0], layer, "test");
        proxy.SetAnchor(anchor);
        proxy.SetSize(0, (uint)height);
        proxy.SetExclusiveZone(exclusive);
        var size = (Width: 0, Height: 0);
        var serial = 0u;
        proxy.Configure += (_, e) =>
        {
            serial = e.Serial;
            size = ((int)e.Width, (int)e.Height);
            proxy.AckConfigure(e.Serial);
            if (size.Width > 0 && size.Height > 0)
            {
                var buffer = client.CreateBuffer(size.Width, size.Height, Fill.Solid(size.Width, size.Height, 0xFF285577));
                surface.Attach(buffer.Proxy, 0, 0);
                surface.Damage(0, 0, size.Width, size.Height);
            }

            surface.Commit();
        };
        surface.Commit();
        host.PumpUntil(() => serial != 0);
        host.PumpToServer();
        return (proxy, () => size);
    }

    [Fact]
    public void A_confined_background_arranges_into_the_box_the_consumer_names()
    {
        using var host = new CompositorTestHost();
        var layerShell = new LayerShell(host.Display, host.Compositor);
        var layers = new SceneLayers(host.Scene.Root);
        var driver = new LayerShellSceneDriver(layerShell, host.Layout, layers);
        var seenUsable = new List<Basin.Box>();
        driver.Confine = (layer, _) => layer.Layer == LayerKind.Background;
        driver.ConfinedBox = (_, output, usable) =>
        {
            seenUsable.Add(usable);
            var box = host.Layout.BoxOf(output);
            return new Basin.Box(box.X + 10, box.Y + usable.Y, box.Width - 20, usable.Height);
        };
        var nested = 0;
        driver.UsableAreaChanged += (_, _) =>
        {
            if (nested++ < 3)
            {
                driver.Rearrange();
            }
        };
        var arranged = 0;
        driver.Arranged += () => arranged++;

        var shellProxy = BindLayerShell(host, host.Client);
        var all = Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Top | Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Bottom |
            Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Left | Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Right;
        var top = Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Top | Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Left |
            Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.Anchor.Right;
        var (_, panelSize) = MapLayer(host, shellProxy, Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, top, 8, 8);
        var (_, wallpaperSize) = MapLayer(host, shellProxy, Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Background, all, 0, -1);
        host.PumpUntil(() => driver.Surfaces.Count == 2 && driver.Surfaces[1].Scene is not null);

        var output = host.Layout.BoxOf(host.Output);
        Assert.Equal(output.Width, panelSize().Width);
        Assert.Equal((output.Width - 20, output.Height - 8), wallpaperSize());
        Assert.Contains(seenUsable, usable => usable.Y == 8);
        var wallpaper = driver.Surfaces[1].Scene!;
        Assert.Equal(output.X + 10, wallpaper.Tree.X);
        Assert.Equal(output.Y + 8, wallpaper.Tree.Y);
        Assert.True(arranged > 0);
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
