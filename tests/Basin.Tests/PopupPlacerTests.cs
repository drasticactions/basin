using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class PopupPlacerTests
{
    [Fact]
    public void A_popup_lands_under_the_parent_tree_at_its_surface_position()
    {
        using var host = new CompositorTestHost();
        var placer = new PopupPlacer(host.Layout);
        var parentTree = new SceneTree(host.Scene.Root);
        parentTree.SetPosition(20, 10);

        XdgPopupWindow? serverPopup = null;
        SceneSurface? placed = null;
        host.Shell.NewPopup += popup =>
        {
            serverPopup = popup;
            placed = placer.Attach(popup, parentTree);
        };

        var client = host.Client;
        var parent = MappedToplevel.Map(host, client);
        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(30, 30);
        positioner.SetAnchorRect(5, 5, 1, 1);
        var popupSurface = client.Compositor.CreateSurface();
        var popupXdg = client.WmBase.GetXdgSurface(popupSurface);
        var popupProxy = popupXdg.GetPopup(parent.XdgSurface, positioner);
        popupXdg.Configure += (_, e) => popupXdg.AckConfigure(e.Serial);
        popupSurface.Commit();
        host.PumpUntil(() => serverPopup is not null);

        var buffer = client.CreateBuffer(30, 30, Fill.Solid(30, 30, 0xFF884422));
        popupSurface.Attach(buffer.Proxy, 0, 0);
        popupSurface.Commit();
        host.PumpToServer();

        Assert.Same(parentTree, placed!.Tree.Parent);
        Assert.Equal(serverPopup!.SurfacePosition.X, placed.Tree.X);
        Assert.Equal(serverPopup.SurfacePosition.Y, placed.Tree.Y);

        popupProxy.Destroy();
        popupXdg.Destroy();
        popupSurface.Dispose();
        host.PumpToServer();
        Assert.True(placed.IsDestroyed);
    }
}
