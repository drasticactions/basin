using Basin.Capabilities;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// Shows the frames of an <see cref="IUISurface"/> in a <see cref="SceneBuffer"/>.
/// It publishes a new frame each time the surface reports damage, and it can create the surface itself from an <see cref="IUIHost"/>.
/// </summary>
public sealed class UISurfaceNode : IUISurfaceObserver, ISceneInputFilter, IDisposable
{
    private readonly SceneBuffer _node;
    private readonly IUIHost? _host;
    private readonly UISurfaceIndex? _index;
    private readonly bool _ownsSurface;
    private IUISurface? _surface;
    private UIFrame _shown;
    private bool _hasShown;
    private bool _faulted;
    private bool _disposed;
    private int _width;
    private int _height;
    private double _scale = 1.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="UISurfaceNode"/> class for a surface that the caller owns.
    /// Disposal of the node does not dispose the surface.
    /// </summary>
    /// <param name="parent">The scene tree that holds the buffer node.</param>
    /// <param name="surface">The surface to show. Its current size sets the initial size and scale.</param>
    /// <param name="index">The index to record the surface in, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> or <paramref name="surface"/> is <see langword="null"/>.</exception>
    public UISurfaceNode(SceneTree parent, IUISurface surface, UISurfaceIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(surface);

        _node = new SceneBuffer(parent) { Enabled = false };
        _surface = surface;
        _index = index;
        _width = surface.Size.Width;
        _height = surface.Size.Height;
        _scale = surface.Size.Scale;
        surface.AddObserver(this);
        _index?.Add(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UISurfaceNode"/> class that creates and owns its surface.
    /// The first successful <see cref="Configure(int, int, double)"/> creates the surface, and disposal of the node disposes it.
    /// </summary>
    /// <param name="parent">The scene tree that holds the buffer node.</param>
    /// <param name="host">The host that creates the surface.</param>
    /// <param name="index">The index to record the surface in, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> or <paramref name="host"/> is <see langword="null"/>.</exception>
    public UISurfaceNode(SceneTree parent, IUIHost host, UISurfaceIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(host);

        _node = new SceneBuffer(parent) { Enabled = false };
        _host = host;
        _index = index;
        _ownsSurface = true;
    }

    /// <summary>
    /// Gets the scene buffer that shows the surface. It starts disabled, and this object destroys it at disposal.
    /// </summary>
    public SceneBuffer Node => _node;

    /// <summary>
    /// Gets the surface. It is <see langword="null"/> before the host creates it, after the surface is destroyed and after disposal.
    /// </summary>
    public IUISurface? Surface => _surface;

    /// <summary>
    /// Gets the horizontal position that the last <see cref="SetPosition(int, int)"/> applied, relative to the parent tree.
    /// </summary>
    public int X { get; private set; }

    /// <summary>
    /// Gets the vertical position that the last <see cref="SetPosition(int, int)"/> applied, relative to the parent tree.
    /// </summary>
    public int Y { get; private set; }

    /// <summary>
    /// Gets the surface width in logical pixels from the last configure or published frame.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Gets the surface height in logical pixels from the last configure or published frame.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Gets the surface scale from the last configure or published frame.
    /// </summary>
    public double Scale => _scale;

    /// <summary>
    /// Gets a value indicating whether the host failed to create the surface. A faulted node refuses every later configure and publish.
    /// </summary>
    public bool IsFaulted => _faulted;

    /// <summary>
    /// Gets or sets the size in device pixels that the node shows from each buffer. When it is <see langword="null"/>, the node shows the whole logical surface.
    /// </summary>
    public (int Width, int Height)? DevicePixels { get; set; }

    /// <summary>
    /// Gets or sets the output scale that converts <see cref="DevicePixels"/> to the logical size of the node. It applies only when <see cref="DevicePixels"/> is set.
    /// </summary>
    public double OutputScale { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the offset in device pixels that aligns the node with the device pixel grid.
    /// </summary>
    public (int X, int Y) DeviceNudge
    {
        get => _node.DeviceNudge;
        set => _node.DeviceNudge = value;
    }

    /// <summary>
    /// Gets or sets the factor that converts node-local input coordinates to surface coordinates.
    /// </summary>
    public double InputScale { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets a value indicating whether the surface decides which points of the node accept input.
    /// When it is <see langword="false"/>, the whole node box accepts input.
    /// </summary>
    public bool InputFromSurface
    {
        get => ReferenceEquals(_node.InputFilter, this);
        set => _node.InputFilter = value ? this : null;
    }

    /// <summary>
    /// Gets or sets a value indicating whether a published frame enables the node. The default is <see langword="true"/>.
    /// </summary>
    public bool AutoEnable { get; set; } = true;

    /// <summary>
    /// Gets the buffer target that the node asks the host for. When it is <see langword="null"/>, the node asks for dmabuf if the host produces it, and memory if not.
    /// </summary>
    public UITargetKind? Target { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the node is drawn.
    /// </summary>
    public bool Enabled
    {
        get => _node.Enabled;
        set => _node.Enabled = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether screen capture leaves the node out.
    /// </summary>
    public bool ExcludedFromCapture
    {
        get => _node.ExcludedFromCapture;
        set => _node.ExcludedFromCapture = value;
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
    /// Occurs once, when the host throws or declines while the node creates its surface. The argument describes the failure.
    /// </summary>
    public event Action<Exception>? Faulted;

    /// <summary>
    /// Places the node at a box in device pixels. It sets the position, <see cref="OutputScale"/>, <see cref="InputScale"/>, <see cref="DeviceNudge"/> and <see cref="DevicePixels"/>.
    /// The caller must then configure the surface at the returned chrome scale.
    /// </summary>
    /// <param name="originX">The horizontal origin in logical pixels that the device box is relative to.</param>
    /// <param name="originY">The vertical origin in logical pixels that the device box is relative to.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="device">The box in device pixels, relative to the origin.</param>
    /// <returns>The integer scale that the surface must draw at.</returns>
    public int PlaceDevice(int originX, int originY, double scale, in Box device)
    {
        var chrome = DevicePlacement.ChromeScale(scale);
        var (x, nudgeX) = DevicePlacement.FromDevice(device.X, scale);
        var (y, nudgeY) = DevicePlacement.FromDevice(device.Y, scale);
        SetPosition(originX + x, originY + y);
        OutputScale = scale;
        InputScale = scale / chrome;
        DeviceNudge = (nudgeX, nudgeY);
        DevicePixels = (device.Width, device.Height);
        return chrome;
    }

    /// <summary>
    /// Moves the node relative to its parent tree.
    /// </summary>
    /// <param name="x">The horizontal position in logical pixels.</param>
    /// <param name="y">The vertical position in logical pixels.</param>
    public void SetPosition(int x, int y)
    {
        X = x;
        Y = y;
        _node.SetPosition(x, y);
    }

    /// <summary>
    /// Sets the surface size and scale. A node that owns its surface creates it on the first call.
    /// The new size shows only after the next <see cref="Publish"/>.
    /// </summary>
    /// <param name="width">The width in logical pixels.</param>
    /// <param name="height">The height in logical pixels.</param>
    /// <param name="scale">The scale that the surface draws at.</param>
    /// <returns><see langword="false"/> after disposal or a fault, for a size or scale of 0 or less, or when the surface declines the size.</returns>
    public bool Configure(int width, int height, double scale)
    {
        if (_disposed || _faulted || width <= 0 || height <= 0 || scale <= 0)
        {
            return false;
        }

        if (_surface is null)
        {
            if (!_ownsSurface || _host is null)
            {
                return false;
            }

            IUISurface? created;
            try
            {
                created = _host.CreateSurface(new UISurfaceOptions
                {
                    Target = Target ?? ((_host.Produces & UITargetKind.Dmabuf) != 0
                        ? UITargetKind.Dmabuf
                        : UITargetKind.Memory),
                    Width = width,
                    Height = height,
                    Scale = scale,
                });
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
            {
                Fault(error);
                return false;
            }

            if (created is null)
            {
                Fault(new InvalidOperationException("The UI host declined to create a surface."));
                return false;
            }

            _surface = created;
            _surface.AddObserver(this);
            _index?.Add(this);
        }
        else if (!_surface.Configure(width, height, scale))
        {
            return false;
        }

        _width = width;
        _height = height;
        _scale = scale;
        if (DevicePixels is { } pixels)
        {
            _node.DestinationWidth = (int)Math.Ceiling((pixels.Width + Math.Max(0, DeviceNudge.X)) / OutputScale);
            _node.DestinationHeight = (int)Math.Ceiling((pixels.Height + Math.Max(0, DeviceNudge.Y)) / OutputScale);
        }
        else
        {
            _node.DestinationWidth = width;
            _node.DestinationHeight = height;
        }

        return true;
    }

    /// <summary>
    /// Acquires the latest frame of the surface and shows it in <see cref="Node"/>. The node keeps the frame until a newer one replaces it.
    /// </summary>
    /// <returns><see langword="true"/> when a new frame is now shown. <see langword="false"/> when no frame is ready, after a fault or after disposal.</returns>
    public bool Publish()
    {
        if (_disposed || _faulted || _surface is null || !_surface.TryAcquire(out var frame))
        {
            return false;
        }

        if (frame.Buffer is null)
        {
            frame.Dispose();
            return false;
        }

        var size = _surface.Size;
        _width = size.Width;
        _height = size.Height;
        _scale = size.Scale;
        _node.SetBuffer(frame.Buffer);
        _node.AcquireFenceFd = frame.AcquireFenceFd;
        if (DevicePixels is { } pixels)
        {
            _node.SourceBox = new FBox(0, 0, Math.Min(pixels.Width, frame.Buffer.Width), Math.Min(pixels.Height, frame.Buffer.Height));
            _node.DestinationWidth = (int)Math.Ceiling((pixels.Width + Math.Max(0, DeviceNudge.X)) / OutputScale);
            _node.DestinationHeight = (int)Math.Ceiling((pixels.Height + Math.Max(0, DeviceNudge.Y)) / OutputScale);
            _node.DevicePixels = (Math.Min(pixels.Width, frame.Buffer.Width), Math.Min(pixels.Height, frame.Buffer.Height));
            _node.DeviceScale = OutputScale;
        }
        else
        {
            _node.DevicePixels = null;
            _node.SourceBox = OutputScaling.ToPhysical(new Box(0, 0, size.Width, size.Height), size.Scale);
            _node.DestinationWidth = size.Width;
            _node.DestinationHeight = size.Height;
        }
        if (AutoEnable)
        {
            _node.Enabled = true;
        }

        if (frame.Damage is { } damage)
        {
            _node.NotifyContentChanged(damage);
        }
        else
        {
            _node.NotifyContentChanged();
        }

        if (InputFromSurface && _node.RootOwner() is { } scene)
        {
            scene.NotifyInputRegionChanged();
        }

        if (_hasShown)
        {
            _shown.Dispose();
        }

        _shown = frame;
        _hasShown = true;
        return true;
    }

    /// <summary>
    /// Publishes the latest frame of the surface. The damage region of the acquired frame goes to the scene, not <paramref name="damage"/>.
    /// </summary>
    /// <param name="surface">The surface that changed.</param>
    /// <param name="damage">The changed region. The node does not read it.</param>
    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage) => Publish();

    bool ISceneInputFilter.AcceptsInputAt(SceneBuffer buffer, double x, double y) =>
        _surface is { } surface && surface.AcceptsInputAt(x * InputScale, y * InputScale);

    /// <inheritdoc/>
    public void OnSurfaceDestroyed(IUISurface surface)
    {
        if (ReferenceEquals(surface, _surface))
        {
            _index?.Remove(this);
            _surface = null;
        }
    }

    /// <summary>
    /// Destroys the scene buffer and releases the shown frame. It disposes the surface only when the node created it. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _index?.Remove(this);
        _surface?.RemoveObserver(this);
        _node.SetBuffer(null);
        _node.AcquireFenceFd = -1;
        if (_hasShown)
        {
            _shown.Dispose();
            _hasShown = false;
        }

        if (!_node.IsDestroyed)
        {
            _node.Destroy();
        }

        if (_ownsSurface)
        {
            _surface?.Dispose();
        }

        _surface = null;
    }

    private void Fault(Exception error)
    {
        _faulted = true;
        Faulted?.Invoke(error);
    }
}
