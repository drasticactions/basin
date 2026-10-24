using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

/// <summary>
/// Puts xdg popups into the scene under the scene surface of their parent, for parents that the consumer registered.
/// </summary>
public sealed class NestedPopupPlacer
{
    private readonly OutputLayout? _layout;
    private readonly Dictionary<XdgSurfaceState, SceneSurface> _scenes = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="NestedPopupPlacer"/> class.
    /// </summary>
    /// <param name="layout">The layout whose outputs constrain popups, or <see langword="null"/> to constrain only through <see cref="ConstrainBox"/>.</param>
    public NestedPopupPlacer(OutputLayout? layout = null)
    {
        _layout = layout;
    }

    /// <summary>
    /// Gets or sets a function that gives the box in layout coordinates for the popups of a toplevel. When set, it replaces the output box. A <see langword="null"/> result leaves the popup unconstrained.
    /// </summary>
    public Func<XdgToplevelWindow, Box?>? ConstrainBox { get; set; }

    /// <summary>
    /// Occurs when a popup moves or is destroyed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Registers the scene surface of a popup parent, or replaces it.
    /// </summary>
    /// <param name="xdg">The parent's xdg surface.</param>
    /// <param name="scene">The parent's scene surface.</param>
    /// <exception cref="ArgumentNullException"><paramref name="xdg"/> or <paramref name="scene"/> is <see langword="null"/>.</exception>
    public void Add(XdgSurfaceState xdg, SceneSurface scene)
    {
        ArgumentNullException.ThrowIfNull(xdg);
        ArgumentNullException.ThrowIfNull(scene);
        _scenes[xdg] = scene;
    }

    /// <summary>
    /// Removes a registered parent. An unknown parent is ignored.
    /// </summary>
    /// <param name="xdg">The parent's xdg surface.</param>
    /// <exception cref="ArgumentNullException"><paramref name="xdg"/> is <see langword="null"/>.</exception>
    public void Remove(XdgSurfaceState xdg)
    {
        ArgumentNullException.ThrowIfNull(xdg);
        _scenes.Remove(xdg);
    }

    /// <summary>
    /// Finds the scene surface of a registered parent or an attached popup.
    /// </summary>
    /// <param name="xdg">The xdg surface to look up.</param>
    /// <param name="scene">The scene surface, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the surface is known.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xdg"/> is <see langword="null"/>.</exception>
    public bool TryGet(XdgSurfaceState xdg, out SceneSurface scene)
    {
        ArgumentNullException.ThrowIfNull(xdg);
        return _scenes.TryGetValue(xdg, out scene!);
    }

    /// <summary>
    /// Follows the popup parents up to the first non-popup parent.
    /// </summary>
    /// <param name="popup">The popup to start at.</param>
    /// <returns>The toplevel at the root, or <see langword="null"/> when the root is not an xdg toplevel.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="popup"/> is <see langword="null"/>.</exception>
    public static XdgToplevelWindow? RootOf(XdgPopupWindow popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        var xdg = popup.Parent;
        while (xdg?.Role is XdgPopupWindow parent)
        {
            xdg = parent.Parent;
        }

        return xdg?.Role as XdgToplevelWindow;
    }

    /// <summary>
    /// Creates the popup's scene surface under its parent's scene surface and registers it, so nested popups can attach to it.
    /// </summary>
    /// <param name="popup">The popup to place.</param>
    /// <returns>The popup's scene surface, or <see langword="null"/> when its parent is not registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="popup"/> is <see langword="null"/>.</exception>
    public SceneSurface? Attach(XdgPopupWindow popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        if (popup.Parent is not { } parent || !_scenes.TryGetValue(parent, out var parentScene))
        {
            return null;
        }

        var scene = new SceneSurface(parentScene.Tree, popup.Surface);
        _scenes[popup.Xdg] = scene;

        void Place()
        {
            var origin = parent.EffectiveGeometry;
            var offset = popup.SurfacePosition;
            scene.Tree.SetPosition(origin.X + offset.X, origin.Y + offset.Y);
            Changed?.Invoke();
        }

        void Constrain()
        {
            if (!parentScene.Tree.TryMapSceneToLocal(0, 0, out var localX, out var localY))
            {
                return;
            }

            var origin = parent.EffectiveGeometry;
            var originX = (int)-localX + origin.X;
            var originY = (int)-localY + origin.Y;
            if (BoxFor(popup, originX, originY) is not { } box)
            {
                return;
            }

            popup.Unconstrain(new Box(box.X - originX, box.Y - originY, box.Width, box.Height));
        }

        Constrain();
        Place();
        popup.Xdg.Committed += Place;
        popup.GeometryChanged += Place;
        popup.Repositioned += Constrain;
        popup.Destroyed += () =>
        {
            popup.Xdg.Committed -= Place;
            popup.GeometryChanged -= Place;
            popup.Repositioned -= Constrain;
            _scenes.Remove(popup.Xdg);
            Changed?.Invoke();
        };
        return scene;
    }

    private Box? BoxFor(XdgPopupWindow popup, int originX, int originY)
    {
        if (ConstrainBox is { } choose)
        {
            return RootOf(popup) is { } root ? choose(root) : null;
        }

        if (_layout is not { } layout)
        {
            return null;
        }

        return layout.OutputAt(originX, originY) is { } output ? layout.BoxOf(output) : layout.Bounds;
    }
}
