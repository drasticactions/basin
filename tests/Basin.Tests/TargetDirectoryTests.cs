using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class TargetDirectoryTests
{
    [Fact]
    public void Popups_are_listed_under_their_toplevel_with_nested_ones_in_creation_order()
    {
        using var host = new CompositorTestHost();
        using var popups = new XdgPopupDirectory(host.Shell);
        var window = MappedToplevel.Map(host, host.Client, 60, 50, 0xFF0000FF);
        var other = MappedToplevel.Map(host, host.Client, 30, 20, 0xFF00FF00);
        var created = new List<XdgPopupWindow>();
        host.Shell.NewPopup += created.Add;

        var first = Popup(host, window.XdgSurface);
        host.PumpUntil(() => created.Count == 1);
        var nested = Popup(host, first.Xdg);
        host.PumpUntil(() => created.Count == 2);

        var found = new List<Surface>();
        popups.CollectPopups(window.ServerSurface, found);
        Assert.Equal([created[0].Surface, created[1].Surface], found);
        found.Clear();
        popups.CollectPopups(other.ServerSurface, found);
        Assert.Empty(found);

        nested.Surface.Dispose();
        host.PumpToServer();
        found.Clear();
        popups.CollectPopups(window.ServerSurface, found);
        Assert.Equal([created[0].Surface], found);
    }

    [Fact]
    public void Layers_are_listed_by_namespace_once_mapped_and_a_surface_is_located_in_the_scene()
    {
        using var host = new CompositorTestHost();
        var layerShell = new LayerShell(host.Display, host.Compositor);
        using var layers = new LayerDirectory(layerShell);
        LayerSurface? serverLayer = null;
        layerShell.NewSurface += layer =>
        {
            serverLayer = layer;
            layer.InitialCommit += () => layer.Configure(160, 30);
        };

        var client = host.Client;
        var shellProxy = LayerShellProtocolTests.BindLayerShell(host, client);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetSize(0, 30);
        layerProxy.Configure += (_, e) => layerProxy.AckConfigure(e.Serial);
        surface.Commit();
        host.PumpUntil(() => serverLayer is not null);

        var found = new List<LayerInfo>();
        layers.CollectLayers(found);
        Assert.Empty(found);

        var buffer = client.CreateBuffer(160, 30, (data, stride) => { });
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpUntil(() => serverLayer!.Surface.IsMapped);
        layers.CollectLayers(found);
        Assert.Equal("panel", Assert.Single(found).Namespace);

        var window = MappedToplevel.Map(host, client, 60, 50, 0xFF0000FF);
        var diagnostics = new SceneDiagnostics(host.Scene, host.Renderer, host.Loop, outputs: null);
        var scene = host.SurfaceScenes.Single(s => ReferenceEquals(s.Surface, window.ServerSurface));
        var box = diagnostics.LocateSurface(window.ServerSurface, 2)!();
        var (x, y) = scene.Tree.OffsetFromRoot;
        Assert.Equal(new Box(x - 2, y - 2, 64, 54), box);

        surface.Dispose();
        host.PumpToServer();
        layerShell.Dispose();
    }

    [Fact]
    public void A_node_path_names_one_node_or_says_how_many()
    {
        using var host = new CompositorTestHost();
        var diagnostics = new SceneDiagnostics(host.Scene, host.Renderer, host.Loop, outputs: null);
        var tree = new SceneTree(host.Scene.Root) { DebugName = "probe" };
        tree.SetPosition(10, 20);
        _ = new SceneRect(tree, 8, 6, new RenderColor(1f, 0f, 0f, 1f)) { DebugName = "dot" };
        _ = new SceneRect(tree, 4, 4, new RenderColor(1f, 0f, 0f, 1f)) { DebugName = "twin" };
        _ = new SceneRect(tree, 4, 4, new RenderColor(1f, 0f, 0f, 1f)) { DebugName = "twin" };

        Assert.Equal(new Box(10, 20, 8, 6), diagnostics.LocateNode("tree/probe/dot", 0, out var error)!());
        Assert.Null(error);
        Assert.Equal(new Box(9, 19, 10, 8), diagnostics.LocateNode("tree/probe", 1, out _)!());
        Assert.Null(diagnostics.LocateNode("tree/probe/twin", 0, out error));
        Assert.Contains("names 2 nodes", error, StringComparison.Ordinal);
        Assert.Null(diagnostics.LocateNode("tree/none", 0, out error));
        tree.Destroy();
    }

    private static (Basin.Shell.Xdg.Protocol.XdgSurface Xdg, Wayland.WlSurface Surface) Popup(CompositorTestHost host, Basin.Shell.Xdg.Protocol.XdgSurface parent)
    {
        var client = host.Client;
        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(20, 10);
        positioner.SetAnchorRect(0, 0, 5, 5);
        var surface = client.Compositor.CreateSurface();
        var xdg = client.WmBase.GetXdgSurface(surface);
        _ = xdg.GetPopup(parent, positioner);
        var configured = false;
        xdg.Configure += (_, e) =>
        {
            xdg.AckConfigure(e.Serial);
            configured = true;
        };
        surface.Commit();
        host.PumpUntil(() => configured);
        var buffer = client.CreateBuffer(20, 10, (data, stride) => { });
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        return (xdg, surface);
    }
}
