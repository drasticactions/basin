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
        Func<Box>? constrainBox = null,
        Func<double>? scale = null)
    {
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(parentTree);

        var frame = scale is null ? null : new SceneTransform(parentTree);
        var scene = new SceneSurface(frame ?? parentTree, popup.Surface);

        Point Origin() => origin?.Invoke() ?? ScenePositionOf(parentTree);

        double Scale()
        {
            var k = scale?.Invoke() ?? 1.0;
            return k > 0 && k < 1.0 ? k : 1.0;
        }

        void Place()
        {
            var treePosition = ScenePositionOf(parentTree);
            var contentOrigin = Origin();
            var chain = ChainOffset(popup);
            var localX = chain.X + popup.SurfacePosition.X;
            var localY = chain.Y + popup.SurfacePosition.Y;
            var k = Scale();
            if (frame is null || k == 1.0)
            {
                if (frame is not null)
                {
                    frame.Matrix = RenderTransform.Identity;
                }

                scene.Tree.SetPosition(contentOrigin.X - treePosition.X + localX, contentOrigin.Y - treePosition.Y + localY);
                return;
            }

            scene.Tree.SetPosition(localX, localY);
            frame.Matrix = new RenderTransform(
                k, 0, contentOrigin.X - treePosition.X,
                0, k, contentOrigin.Y - treePosition.Y,
                0, 0, 1);
        }

        void Constrain()
        {
            var contentOrigin = Origin();
            var chain = ChainOffset(popup);
            var k = Scale();
            Box box;
            if (constrainBox is not null)
            {
                box = constrainBox();
            }
            else
            {
                var output = _layout.OutputAt(contentOrigin.X + (int)Math.Round(chain.X * k), contentOrigin.Y + (int)Math.Round(chain.Y * k));
                box = output is null ? _layout.Bounds : _layout.BoxOf(output);
            }

            var left = (box.X - contentOrigin.X) / k;
            var top = (box.Y - contentOrigin.Y) / k;
            var x = (int)Math.Ceiling(left);
            var y = (int)Math.Ceiling(top);
            popup.Unconstrain(new Box(
                x - chain.X,
                y - chain.Y,
                (int)Math.Floor(left + (box.Width / k)) - x,
                (int)Math.Floor(top + (box.Height / k)) - y));
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
            frame?.Destroy();
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
