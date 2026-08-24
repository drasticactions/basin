using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

public sealed class PopupPlacer
{
    private readonly OutputLayout _layout;

    public PopupPlacer(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    public static Point ChainOffset(XdgPopupWindow popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        var x = 0;
        var y = 0;
        var xdg = popup.Parent;
        while (xdg?.Role is XdgPopupWindow parent)
        {
            x += parent.Geometry.X;
            y += parent.Geometry.Y;
            xdg = parent.Parent;
        }

        return new Point(x, y);
    }

    public SceneSurface Attach(
        XdgPopupWindow popup,
        SceneTree parentTree,
        Func<Point>? origin = null,
        Func<Box>? constrainBox = null)
    {
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(parentTree);

        var scene = new SceneSurface(parentTree, popup.Surface);

        Point Origin() => origin?.Invoke() ?? ScenePositionOf(parentTree);

        void Place()
        {
            var treePosition = ScenePositionOf(parentTree);
            var contentOrigin = Origin();
            var chain = ChainOffset(popup);
            scene.Tree.SetPosition(
                contentOrigin.X - treePosition.X + chain.X + popup.SurfacePosition.X,
                contentOrigin.Y - treePosition.Y + chain.Y + popup.SurfacePosition.Y);
        }

        void Constrain()
        {
            var contentOrigin = Origin();
            var chain = ChainOffset(popup);
            var originX = contentOrigin.X + chain.X;
            var originY = contentOrigin.Y + chain.Y;
            Box box;
            if (constrainBox is not null)
            {
                box = constrainBox();
            }
            else
            {
                var output = _layout.OutputAt(originX, originY);
                box = output is null ? _layout.Bounds : _layout.BoxOf(output);
            }

            popup.Unconstrain(new Box(box.X - originX, box.Y - originY, box.Width, box.Height));
        }

        Constrain();
        Place();
        popup.Xdg.Committed += Place;
        popup.GeometryChanged += Place;
        popup.Repositioned += Constrain;
        scene.Destroyed += () =>
        {
            popup.Xdg.Committed -= Place;
            popup.GeometryChanged -= Place;
            popup.Repositioned -= Constrain;
        };
        popup.Destroyed += () =>
        {
            if (!scene.IsDestroyed)
            {
                scene.Destroy();
            }
        };
        return scene;
    }

    private static Point ScenePositionOf(SceneTree tree) =>
        tree.TryMapSceneToLocal(0, 0, out var localX, out var localY)
            ? new Point((int)-localX, (int)-localY)
            : new Point(tree.X, tree.Y);
}
