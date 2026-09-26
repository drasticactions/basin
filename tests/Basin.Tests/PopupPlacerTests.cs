using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;
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

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    public void A_scaled_popup_draws_at_the_parent_scale_and_is_constrained_as_it_draws(double scale)
    {
        using var host = new CompositorTestHost();
        var placer = new PopupPlacer(host.Layout);
        var parentTree = new SceneTree(host.Scene.Root);
        parentTree.SetPosition(20, 10);
        var width = host.Layout.BoxOf(host.Output).Width;
        var origin = new Point(width - 60, 100);

        XdgPopupWindow? serverPopup = null;
        SceneSurface? placed = null;
        host.Shell.NewPopup += popup =>
        {
            serverPopup = popup;
            placed = placer.Attach(popup, parentTree, origin: () => origin, scale: () => scale);
        };

        var client = host.Client;
        var parent = MappedToplevel.Map(host, client);
        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(30, 30);
        positioner.SetAnchorRect(40, 40, 1, 1);
        positioner.SetAnchor(XdgPositioner.Anchor.BottomRight);
        positioner.SetGravity(XdgPositioner.Gravity.BottomRight);
        positioner.SetConstraintAdjustment(XdgPositioner.ConstraintAdjustment.SlideX);
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

        var position = serverPopup!.SurfacePosition;
        if (scale == 1.0)
        {
            var flat = Assert.IsType<SceneTransform>(placed!.Tree.Parent);
            Assert.True(flat.Matrix.IsIdentity);
            Assert.Equal(origin.X - parentTree.X + position.X, placed.Tree.X);
            Assert.Equal(30, width - (origin.X + position.X));
        }
        else
        {
            Assert.Equal(41, position.X);
            Assert.Equal(41, position.Y);
            var frame = Assert.IsType<SceneTransform>(placed!.Tree.Parent);
            Assert.Same(parentTree, frame.Parent);
            var (drawnX, drawnY) = frame.Matrix.Map(placed.Tree.X, placed.Tree.Y);
            Assert.Equal(origin.X - parentTree.X + (scale * position.X), drawnX, 9);
            Assert.Equal(origin.Y - parentTree.Y + (scale * position.Y), drawnY, 9);
            Assert.Equal(scale, frame.Matrix.M11);
            Assert.True(origin.X + ((position.X + 30) * scale) <= width, "the scaled popup draws inside the output");
        }

        popupProxy.Destroy();
        popupXdg.Destroy();
        popupSurface.Dispose();
        host.PumpToServer();
        Assert.True(placed.IsDestroyed);
        Assert.True(placed.Tree.Parent is null);
    }
}
