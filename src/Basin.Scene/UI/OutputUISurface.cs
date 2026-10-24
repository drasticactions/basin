using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// A UI surface that covers an output box, such as a panel, a wallpaper or an overlay.
/// It creates its surface from an <see cref="IUIHost"/> on the first successful placement and can draw at a zoom.
/// </summary>
public sealed class OutputUISurface : IDisposable
{
    private readonly UISurfaceNode _node;
    private Box _bounds;
    private double _scale = 1.0;
    private bool _placed;
    private bool _disposed;
    private bool _zoomed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputUISurface"/> class.
    /// No surface exists until the first successful <see cref="Place(in Box, double)"/> or <see cref="PlaceAt(in Box, double)"/>.
    /// </summary>
    /// <param name="parent">The scene tree that holds the node.</param>
    /// <param name="host">The host that creates the surface.</param>
    /// <param name="index">The index that records the surface for input routing, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> or <paramref name="host"/> is <see langword="null"/>.</exception>
    public OutputUISurface(SceneTree parent, IUIHost host, UISurfaceIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(host);

        _node = new UISurfaceNode(parent, host, index);
    }

    /// <summary>
    /// Gets the node that shows the surface. This object owns it.
    /// </summary>
    public UISurfaceNode Node => _node;

    /// <summary>
    /// Gets the surface. It is <see langword="null"/> before the first successful placement, after a fault and after disposal.
    /// </summary>
    public IUISurface? Surface => _node.Surface;

    /// <summary>
    /// Gets the box in logical scene coordinates that the last successful placement applied.
    /// </summary>
    public Box Bounds => _bounds;

    /// <summary>
    /// Gets the output scale that the last successful placement applied. It is 1 before the first placement.
    /// </summary>
    public double Scale => _scale;

    /// <summary>
    /// Gets a value indicating whether the surface exists.
    /// </summary>
    public bool IsRealized => _node.Surface is not null;

    /// <summary>
    /// Gets a value indicating whether a placement has succeeded at least once.
    /// </summary>
    public bool IsPlaced => _placed;

    /// <summary>
    /// Gets a value indicating whether the host failed to create the surface. A faulted object refuses every later placement.
    /// </summary>
    public bool IsFaulted => _node.IsFaulted;

    /// <summary>
    /// Gets or sets a value indicating whether a published frame enables the node. The default is <see langword="true"/>.
    /// </summary>
    public bool AutoEnable
    {
        get => _node.AutoEnable;
        set => _node.AutoEnable = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the node takes part in scene hit testing.
    /// </summary>
    public bool InputEnabled
    {
        get => _node.InputEnabled;
        set => _node.InputEnabled = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the node passes the surface damage region to the scene instead of damaging its whole box.
    /// </summary>
    public bool PreciseDamage
    {
        get => _node.PreciseDamage;
        set => _node.PreciseDamage = value;
    }

    /// <summary>
    /// Gets or sets the function that <see cref="Place(in Box, double)"/> uses to map the output box and scale to the surface box.
    /// When it is <see langword="null"/>, the surface covers the whole output box.
    /// </summary>
    public Func<Box, double, Box>? Anchor { get; set; }

    /// <summary>
    /// Gets or sets the zoom factor. A value above 1 draws fewer logical pixels and stretches them over the box. A value of 0 or less acts as 1.
    /// </summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>
    /// Occurs once, when the placement that creates the surface succeeds. The argument is the new surface.
    /// </summary>
    public event Action<IUISurface>? Realized;

    /// <summary>
    /// Gets or sets a value indicating whether the node is drawn.
    /// </summary>
    public bool Enabled
    {
        get => _node.Enabled;
        set => _node.Enabled = value;
    }

    /// <summary>
    /// Places the surface on an output through <see cref="Anchor"/> and then calls <see cref="PlaceAt(in Box, double)"/>.
    /// </summary>
    /// <param name="outputBox">The output box in logical scene coordinates.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns><see langword="true"/> when the surface accepted the placement.</returns>
    public bool Place(in Box outputBox, double scale) =>
        PlaceAt(Anchor is { } anchor ? anchor(outputBox, scale) : outputBox, scale);

    /// <summary>
    /// Moves the node to a box and configures the surface for its size, scale and <see cref="Zoom"/>. The first success creates the surface and raises <see cref="Realized"/>.
    /// It gives the surface the corner of the box mapped through every transform on the path as its position.
    /// </summary>
    /// <param name="box">The surface box in logical scene coordinates.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns><see langword="false"/> after disposal, for an empty box or a scale of 0 or less, or when the surface declines the size.</returns>
    public bool PlaceAt(in Box box, double scale)
    {
        if (_disposed || box.Width <= 0 || box.Height <= 0 || scale <= 0)
        {
            return false;
        }

        var realized = IsRealized;
        _node.SetPosition(box.X, box.Y);
        var zoom = Zoom > 0 ? Zoom : 1.0;
        var configured = zoom == 1.0
            ? Unzoomed(box, scale)
            : Zoomed(box, scale, zoom);
        if (!configured)
        {
            return false;
        }

        _bounds = box;
        _scale = scale;
        _placed = true;

        var shown = _node.Node.MapToScene(new Box(0, 0, box.Width, box.Height));
        var (sceneX, sceneY) = shown.IsEmpty ? _node.Node.OffsetFromRoot : (shown.X, shown.Y);
        _node.Surface?.SetPosition(sceneX, sceneY);

        if (!realized && _node.Surface is { } surface)
        {
            Realized?.Invoke(surface);
        }

        return true;
    }

    /// <summary>
    /// Shows the latest frame of the surface. The node also does this when the surface reports damage.
    /// </summary>
    /// <returns><see langword="true"/> when a new frame is now shown.</returns>
    public bool Publish() => _node.Publish();

    private bool Unzoomed(in Box box, double scale)
    {
        if (_node.DevicePixels is not null && _zoomed)
        {
            _node.DevicePixels = null;
            _node.InputScale = 1.0;
            _node.OutputScale = 1.0;
        }

        _zoomed = false;
        return _node.Configure(box.Width, box.Height, scale);
    }

    private bool Zoomed(in Box box, double scale, double zoom)
    {
        _zoomed = true;
        _node.OutputScale = scale;
        _node.InputScale = 1.0 / zoom;
        _node.DevicePixels = ((int)Math.Round(box.Width * scale), (int)Math.Round(box.Height * scale));
        return _node.Configure(
            Math.Max(1, (int)Math.Ceiling(box.Width / zoom)),
            Math.Max(1, (int)Math.Ceiling(box.Height / zoom)),
            scale * zoom);
    }

    /// <summary>
    /// Destroys the node and disposes the surface. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _node.Dispose();
    }
}
