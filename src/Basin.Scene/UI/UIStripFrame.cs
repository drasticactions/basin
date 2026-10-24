using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// A server-side frame made of four UI surfaces, one per strip, around a content box. A toolkit paints each strip by itself, so a resize repaints only the ring.
/// The frame places the strips as scene nodes under a tree whose origin is the top-left corner of the content, crops their input to the border, and hit tests the titlebar and the edges.
/// </summary>
public sealed class UIStripFrame : IDisposable
{
    private readonly IUIHost _host;
    private readonly UISurfaceIndex? _index;
    private readonly Action<UIFrameStrip, IUISurface> _fill;
    private readonly Strip[] _strips = new Strip[4];
    private string? _accessibleTitle;
    private int _x;
    private int _y;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UIStripFrame"/> class. It makes no surface until the first <see cref="Update"/>.
    /// </summary>
    /// <param name="host">The toolkit that makes the strip surfaces.</param>
    /// <param name="parent">The tree that holds the strip nodes. Its origin is the top-left corner of the content.</param>
    /// <param name="metrics">The margin, border and titlebar sizes.</param>
    /// <param name="fill">The callback that gives a new strip surface its content.</param>
    /// <param name="index">The index that finds a strip surface from its scene node, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/>, <paramref name="parent"/> or <paramref name="fill"/> is <see langword="null"/>.</exception>
    public UIStripFrame(IUIHost host, SceneTree parent, UIStripFrameMetrics metrics, Action<UIFrameStrip, IUISurface> fill, UISurfaceIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(fill);
        _host = host;
        _index = index;
        _fill = fill;
        Parent = parent;
        Metrics = metrics;
    }

    /// <summary>
    /// Gets the tree that holds the strip nodes.
    /// </summary>
    public SceneTree Parent { get; }

    /// <summary>
    /// Gets or sets the name that a screen reader gives the titlebar strip, usually the window title.
    /// The edge strips stay off the accessibility bus. It applies to a strip surface that implements <see cref="IAccessibleUISurface"/>.
    /// </summary>
    public string? AccessibleTitle
    {
        get => _accessibleTitle;
        set
        {
            _accessibleTitle = value;
            if (_strips[(int)UIFrameStrip.Title].Surface is IAccessibleUISurface title)
            {
                title.AccessibleName = value;
            }
        }
    }

    /// <summary>
    /// Gets the margin, border and titlebar sizes.
    /// </summary>
    public UIStripFrameMetrics Metrics { get; }

    /// <summary>
    /// Gets the outer box of the frame in layout coordinates, shadow margin included. It is empty before the first <see cref="Update"/>.
    /// </summary>
    public Box OuterBox => new(_x - Metrics.InsetX, _y - Metrics.InsetY, _width, _height);

    /// <summary>
    /// Gets the surface of one strip, or <see langword="null"/> before the first <see cref="Update"/> made it.
    /// </summary>
    /// <param name="strip">The strip.</param>
    /// <returns>The surface, or <see langword="null"/>.</returns>
    public IUISurface? SurfaceOf(UIFrameStrip strip) => _strips[(int)strip].Surface;

    /// <summary>
    /// Tests whether a surface is one of the four strips.
    /// </summary>
    /// <param name="surface">The surface to test.</param>
    /// <returns><see langword="true"/> when the frame owns <paramref name="surface"/>.</returns>
    public bool OwnsSurface(IUISurface surface)
    {
        foreach (var strip in _strips)
        {
            if (strip.Surface is { } candidate && ReferenceEquals(candidate, surface))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Places the frame around a content box. It makes each strip surface on first use, and configures it again when its size or scale changes.
    /// </summary>
    /// <param name="x">The left edge of the content in layout coordinates.</param>
    /// <param name="y">The top edge of the content in layout coordinates.</param>
    /// <param name="width">The content width.</param>
    /// <param name="height">The content height.</param>
    /// <param name="scale">The output scale for the strip surfaces.</param>
    /// <returns><see langword="false"/> when the frame is disposed or the box is empty, else <see langword="true"/>.</returns>
    public bool Update(int x, int y, int width, int height, double scale)
    {
        if (_disposed || width <= 0 || height <= 0)
        {
            return false;
        }

        var metrics = Metrics;
        var outerWidth = width + (metrics.InsetX * 2);
        var outerHeight = height + metrics.InsetY + metrics.InsetBottom;
        var top = metrics.InsetY;
        var interior = outerHeight - top - metrics.InsetBottom;
        var side = metrics.InsetX;
        _x = x;
        _y = y;
        _width = outerWidth;
        _height = outerHeight;

        Place(UIFrameStrip.Title, new Box(0, 0, outerWidth, top), scale);
        Place(UIFrameStrip.Left, new Box(0, top, side, interior), scale);
        Place(UIFrameStrip.Right, new Box(outerWidth - side, top, side, interior), scale);
        Place(UIFrameStrip.Bottom, new Box(0, outerHeight - metrics.InsetBottom, outerWidth, metrics.InsetBottom), scale);
        return true;
    }

    /// <summary>
    /// Shows or hides every strip node.
    /// </summary>
    /// <param name="enabled"><see langword="true"/> to show the strips.</param>
    public void SetEnabled(bool enabled)
    {
        foreach (var strip in _strips)
        {
            if (strip.Node is { } node)
            {
                node.Enabled = enabled;
            }
        }
    }

    /// <summary>
    /// Tests whether a point in layout coordinates falls on the border or the titlebar, and not on the shadow margin or the content.
    /// </summary>
    /// <param name="x">The point's X in layout coordinates.</param>
    /// <param name="y">The point's Y in layout coordinates.</param>
    /// <returns><see langword="true"/> when the frame takes input at the point.</returns>
    public bool AcceptsInputAt(double x, double y)
    {
        var box = OuterBox;
        var margin = Metrics.Margin;
        var localX = x - box.X;
        var localY = y - box.Y;
        if (localX < margin || localY < margin ||
            localX >= box.Width - margin || localY >= box.Height - margin)
        {
            return false;
        }

        return localX < Metrics.InsetX ||
               localY < Metrics.InsetY ||
               localX >= box.Width - Metrics.InsetX ||
               localY >= box.Height - Metrics.InsetBottom;
    }

    /// <summary>
    /// Tests whether a point in layout coordinates falls on the titlebar.
    /// </summary>
    /// <param name="x">The point's X in layout coordinates.</param>
    /// <param name="y">The point's Y in layout coordinates.</param>
    /// <returns><see langword="true"/> when the point is inside the titlebar.</returns>
    public bool HitsTitlebar(double x, double y)
    {
        var box = OuterBox;
        var margin = Metrics.Margin;
        var localX = x - box.X;
        var localY = y - box.Y;
        return localX >= margin && localX < box.Width - margin &&
               localY >= margin && localY < margin + Metrics.Titlebar;
    }

    /// <summary>
    /// Finds the resize edges at a point in layout coordinates. Only the border band counts, so the titlebar gives no top edge below its first border width.
    /// </summary>
    /// <param name="x">The point's X in layout coordinates.</param>
    /// <param name="y">The point's Y in layout coordinates.</param>
    /// <returns>The edges, or <see cref="FrameEdges.None"/>. The values match <c>xdg_toplevel.resize_edge</c>.</returns>
    public FrameEdges EdgeAt(double x, double y)
    {
        var box = OuterBox;
        var margin = Metrics.Margin;
        var border = Metrics.Border;
        var localX = x - box.X;
        var localY = y - box.Y;
        var edges = FrameEdges.None;

        if (localX >= margin && localX < margin + border)
        {
            edges |= FrameEdges.Left;
        }
        else if (localX >= box.Width - margin - border && localX < box.Width - margin)
        {
            edges |= FrameEdges.Right;
        }

        if (localY >= margin && localY < margin + border)
        {
            edges |= FrameEdges.Top;
        }
        else if (localY >= box.Height - margin - border && localY < box.Height - margin)
        {
            edges |= FrameEdges.Bottom;
        }

        return edges;
    }

    /// <summary>
    /// Destroys the strip nodes and disposes the strip surfaces. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var i = 0; i < _strips.Length; i++)
        {
            _strips[i].Node?.Dispose();
            _strips[i].Surface?.Dispose();
            _strips[i] = default;
        }
    }

    private void Place(UIFrameStrip which, in Box local, double scale)
    {
        var slot = (int)which;
        var strip = _strips[slot];
        if (strip.Surface is null)
        {
            var created = _host.CreateSurface(new UISurfaceOptions
            {
                Target = _host.Produces,
                Width = local.Width,
                Height = local.Height,
                Scale = scale,
            });
            if (created is null)
            {
                return;
            }

            if (created is IAccessibleUISurface accessible)
            {
                accessible.Accessible = which == UIFrameStrip.Title;
                if (which == UIFrameStrip.Title)
                {
                    accessible.AccessibleName = _accessibleTitle;
                }
            }

            _fill(which, created);
            strip = new Strip(created, new UISurfaceNode(Parent, created, _index) { PreciseDamage = true });
            strip.Node!.Node.LowerToBottom();
        }
        else if (local.Width != strip.Width || local.Height != strip.Height || scale != strip.Scale)
        {
            strip.Surface.Configure(local.Width, local.Height, scale);
        }

        _strips[slot] = strip with { Width = local.Width, Height = local.Height, Scale = scale };

        var outer = OuterBox;
        strip.Surface!.SetPosition(outer.X + local.X, outer.Y + local.Y);
        strip.Node!.SetPosition(local.X - Metrics.InsetX, local.Y - Metrics.InsetY);

        var margin = Metrics.Margin;
        var taken = new Box(margin, margin, _width - (margin * 2), _height - (margin * 2)).Intersect(local);
        strip.Node.Node.InputBox = new Box(taken.X - local.X, taken.Y - local.Y, taken.Width, taken.Height);
    }

    private readonly record struct Strip(
        IUISurface? Surface,
        UISurfaceNode? Node,
        int Width = 0,
        int Height = 0,
        double Scale = 0);
}
