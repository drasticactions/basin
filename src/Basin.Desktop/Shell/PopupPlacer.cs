using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

/// <summary>
/// Puts xdg popups into the scene under a parent tree and keeps them in place. It constrains them to an output or a given box.
/// </summary>
public sealed class PopupPlacer
{
    private readonly OutputLayout _layout;
    private readonly List<Action> _placements = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PopupPlacer"/> class.
    /// </summary>
    /// <param name="layout">The layout whose outputs constrain a popup when no box is given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    public PopupPlacer(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    /// <summary>
    /// Places again every popup that was attached with a transform. Call it when a transform changes.
    /// </summary>
    public void PlaceAll()
    {
        for (var i = _placements.Count - 1; i >= 0; i--)
        {
            _placements[i]();
        }
    }

    /// <summary>
    /// Adds up the geometry offsets of the popups above a popup, up to its first non-popup parent.
    /// </summary>
    /// <param name="popup">The popup to measure.</param>
    /// <returns>The offset in surface-local logical pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="popup"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Creates the popup's scene surface and keeps it placed on commit, geometry change and reposition. The scene surface is destroyed with the popup.
    /// </summary>
    /// <param name="popup">The popup to place.</param>
    /// <param name="parentTree">The tree that holds the popup.</param>
    /// <param name="origin">The layout position of the parent content, or <see langword="null"/> to use the scene position of <paramref name="parentTree"/>.</param>
    /// <param name="constrainBox">The box in layout coordinates to keep the popup in, or <see langword="null"/> for the output at the origin.</param>
    /// <param name="scale">The scale of the parent content. Only values between 0 and 1 have an effect.</param>
    /// <param name="transform">An extra transform of the parent content, or <see langword="null"/>.</param>
    /// <returns>The popup's scene surface.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="popup"/> or <paramref name="parentTree"/> is <see langword="null"/>.</exception>
    public SceneSurface Attach(
        XdgPopupWindow popup,
        SceneTree parentTree,
        Func<Point>? origin = null,
        Func<Box>? constrainBox = null,
        Func<double>? scale = null,
        Func<RenderTransform>? transform = null)
    {
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(parentTree);

        var frame = scale is null && transform is null ? null : new SceneTransform(parentTree);
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
            var turn = transform?.Invoke() ?? RenderTransform.Identity;
            if (frame is null || (k == 1.0 && turn.IsIdentity))
            {
                if (frame is not null)
                {
                    frame.Matrix = RenderTransform.Identity;
                }

                scene.Tree.SetPosition(contentOrigin.X - treePosition.X + localX, contentOrigin.Y - treePosition.Y + localY);
                return;
            }

            scene.Tree.SetPosition(localX, localY);
            var placement = new RenderTransform(
                k, 0, contentOrigin.X - treePosition.X,
                0, k, contentOrigin.Y - treePosition.Y,
                0, 0, 1);
            frame.Matrix = turn.IsIdentity
                ? placement
                : RenderTransform.Multiply(
                    RenderTransform.Translation(contentOrigin.X - treePosition.X, contentOrigin.Y - treePosition.Y),
                    RenderTransform.Multiply(turn, RenderTransform.Scale(k, k)));
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
        if (transform is not null)
        {
            _placements.Add(Place);
        }

        scene.Destroyed += () =>
        {
            if (transform is not null)
            {
                _placements.Remove(Place);
            }

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
