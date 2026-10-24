using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Seat;
using static Basin.Desktop.DesktopLog;

namespace Basin.Desktop;

/// <summary>
/// Shows one cursor across the outputs of a layout. It puts the cursor on a hardware cursor plane when the output accepts it, and draws it in the scene when not. In parent mode, it hands the cursor to the host instead.
/// </summary>
public sealed class CursorController : IDisposable
{
    private readonly OutputLayout _layout;
    private readonly List<(IOutput Output, SceneOutput? Scene)> _outputs = [];
    private readonly Dictionary<IOutput, ImageDescription?> _descriptions = [];
    private readonly Dictionary<string, Func<double, XcursorImage?>> _painted = [];

    private CursorImages? _images;
    private IAllocator? _allocator;
    private CursorImage? _showing;
    private bool _hidden;
    private string _showingName = string.Empty;

    private CursorImage? _clientCursor;
    private Surface? _clientSurface;
    private Surface? _hoverSurface;
    private (int X, int Y) _clientHotspot;
    private bool _overClient;
    private bool _clientFramePending;

    private bool _parentMode;
    private IParentCursor? _parent;
    private double _parentScale = 1;

    private IOutput? _cursorOn;
    private double _magnification = 1.0;
    private bool _software;
    private double _x, _y;

    /// <summary>
    /// Initializes a new instance of the <see cref="CursorController"/> class. No cursor shows until <see cref="Load"/> and <see cref="AddOutput"/> are called.
    /// </summary>
    /// <param name="layout">The layout that places the cursor position on outputs.</param>
    public CursorController(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _layout.Changed += Refresh;
    }

    /// <summary>
    /// Gets or sets the capture service that receives the cursor image and position, so that captures can include the cursor. When it is <see langword="null"/>, nothing is published.
    /// </summary>
    public IScreenCapture? Capture { get; set; }

    /// <summary>
    /// Gets or sets the cursor size factor on top of the output scale. A value below 1 becomes 1. A change reloads the theme images.
    /// </summary>
    public double Magnification
    {
        get => _magnification;
        set
        {
            var next = Math.Max(1.0, value);
            if (Math.Abs(next - _magnification) < 1e-6)
            {
                return;
            }

            _magnification = next;
            ReloadForScale();
            if (_clientSurface is not null)
            {
                TakeClientCursor();
            }
            else
            {
                Present(force: true);
            }
        }
    }

    /// <summary>
    /// Gets the theme images. It is <see langword="null"/> before <see cref="Load"/>. Its current variant has the largest output scale and no output description.
    /// </summary>
    public CursorImages? Images => _images;

    /// <summary>
    /// Gets the output that shows the cursor now. It is <see langword="null"/> when the cursor is hidden, in parent mode, or outside every added output.
    /// </summary>
    public IOutput? CursorOutput => _cursorOn;

    /// <summary>
    /// Tests whether the scene draws the cursor on an output, because the hardware cursor plane refused the image.
    /// </summary>
    /// <param name="output">The output to test.</param>
    /// <returns><see langword="true"/> when the cursor is on <paramref name="output"/> and is drawn in the scene.</returns>
    public bool IsSoftwareOn(IOutput output) => ReferenceEquals(_cursorOn, output) && _software;

    /// <summary>
    /// Gets a diagnostic word for the cursor path: <c>none</c> when no theme cursor is loaded, then <c>parent</c>, <c>software</c> or <c>plane</c>.
    /// </summary>
    public string DrawnBy =>
        _images?.Named("left_ptr") is null ? "none"
        : _parentMode ? "parent"
        : _software ? "software"
        : "plane";

    /// <summary>
    /// Gets a diagnostic word for the image: the theme cursor name, <c>client</c> for a client image, or <c>nothing</c>.
    /// </summary>
    public string Showing =>
        _showing is null ? "nothing" : _showingName.Length > 0 ? _showingName : "client";

    /// <summary>
    /// Loads the cursor theme into buffers from an allocator and shows <c>left_ptr</c>. It replaces an earlier load and logs a warning when no theme is found.
    /// </summary>
    /// <param name="allocator">The allocator for cursor buffers. The controller owns it and disposes it.</param>
    /// <param name="bufferWidth">The cursor buffer width in pixels, usually the hardware cursor size.</param>
    /// <param name="bufferHeight">The cursor buffer height in pixels.</param>
    /// <param name="logicalSize">The cursor size in logical pixels.</param>
    public void Load(IAllocator allocator, int bufferWidth, int bufferHeight, int logicalSize = 24)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        _images?.Dispose();
        _allocator?.Dispose();
        _allocator = allocator;
        _images = new CursorImages(allocator, bufferWidth, bufferHeight, logicalSize: logicalSize, scale: MaxScale())
        {
            ColorProfiles = ColorProfiles,
        };
        foreach (var (name, paint) in _painted)
        {
            _images.Define(name, paint);
        }

        if (!_images.HasTheme)
        {
            Log.Warn($"no cursor theme found. XCURSOR_PATH, ~/.local/share/icons, ~/.icons, /usr/share/icons and /usr/share/pixmaps hold none, so no cursor can be drawn");
            return;
        }

        _showingName = string.Empty;
        ShowNamed("left_ptr");
        if (_images.Named("left_ptr") is null)
        {
            Log.Warn($"the cursor theme has no left_ptr or default at {_images.Size}px, or its buffer could not be allocated at {bufferWidth}x{bufferHeight}");
        }
    }

    /// <summary>
    /// Adds an output that can show the cursor.
    /// </summary>
    /// <param name="output">The output to add.</param>
    /// <param name="scene">The scene output that draws a software cursor, or <see langword="null"/> when the output has only a hardware cursor.</param>
    public void AddOutput(IOutput output, SceneOutput? scene)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputs.Add((output, scene));
        output.Committed += OnOutputCommitted;
        output.Frame += OnOutputFrame;
        Refresh();
    }

    /// <summary>
    /// Removes an output and takes the cursor off it.
    /// </summary>
    /// <param name="output">The output to remove.</param>
    public void RemoveOutput(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Committed -= OnOutputCommitted;
        output.Frame -= OnOutputFrame;
        _outputs.RemoveAll(entry => ReferenceEquals(entry.Output, output));
        if (ReferenceEquals(_cursorOn, output))
        {
            _cursorOn = null;
        }

        Refresh();
    }

    /// <summary>
    /// Gets or sets the cursor shape manager whose <see cref="CursorShapeManager.Scale"/> the controller keeps at the largest output scale.
    /// </summary>
    public CursorShapeManager? Shapes { get; set; }

    /// <summary>
    /// Gets or sets the color profile service that converts cursor images for each output. Set it before <see cref="Load"/>, which reads it.
    /// </summary>
    public IColorProfileService? ColorProfiles { get; set; }

    /// <summary>
    /// Sets the image description of an added output, so that the cursor is converted for that output. It does nothing for an output that was not added.
    /// </summary>
    /// <param name="output">The output to describe.</param>
    /// <param name="description">The image description, or <see langword="null"/> for the default.</param>
    public void Describe(IOutput output, ImageDescription? description)
    {
        ArgumentNullException.ThrowIfNull(output);
        for (var i = 0; i < _outputs.Count; i++)
        {
            if (ReferenceEquals(_outputs[i].Output, output))
            {
                _descriptions[output] = description;
                Refresh();
                return;
            }
        }
    }

    private void OnOutputCommitted(OutputStateFields fields)
    {
        const OutputStateFields Reconfigured =
            OutputStateFields.Enabled | OutputStateFields.Scale | OutputStateFields.Transform;
        if ((fields & Reconfigured) == 0)
        {
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (Shapes is { } shapes)
        {
            shapes.Scale = MaxScale();
        }

        _images?.ReloadForScale(MaxScale(), static () => { });
        if (_showingName.Length > 0 && _images?.Named(_showingName, KeyAt(_x, _y)) is { } image)
        {
            _showing = image;
        }

        if (_showingName.Length > 0)
        {
            if (_parentMode)
            {
                if (_showing is { } showing)
                {
                    Show(showing);
                }
            }
            else
            {
                Present(force: true);
            }
        }

        PublishCursor();
    }

    /// <summary>
    /// Switches to parent mode before a parent cursor is attached. The controller then stops drawing the cursor on its outputs.
    /// </summary>
    public void UseParentCursor() => _parentMode = true;

    /// <summary>
    /// Switches to parent mode and hands the current image to a host cursor.
    /// </summary>
    /// <param name="parent">The host cursor that shows the image.</param>
    public void AttachParent(IParentCursor parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _parentMode = true;
        _parent = parent;
        if (_showing is { } image)
        {
            Show(image);
        }
    }

    /// <summary>
    /// Reloads the theme images for the current largest output scale and shows the current named cursor again.
    /// </summary>
    public void ReloadForScale() =>
        _images?.ReloadForScale(MaxScale(), () =>
        {
            if (_showingName is { Length: > 0 } showing)
            {
                _showingName = string.Empty;
                ShowNamed(showing);
            }
        });

    /// <summary>
    /// Defines or replaces a named cursor that code paints instead of the theme. The definition survives later loads.
    /// </summary>
    /// <param name="name">The cursor name.</param>
    /// <param name="paint">The painter, called with the scale. It returns <see langword="null"/> when it cannot paint.</param>
    public void Define(string name, Func<double, XcursorImage?> paint)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(paint);
        _painted[name] = paint;
        _images?.Define(name, paint);
        if (_showingName == name)
        {
            _showingName = string.Empty;
            ShowNamed(name);
        }
    }

    /// <summary>
    /// Shows a named cursor from the theme or from <see cref="Define"/>. It does nothing when the name shows already or is unknown.
    /// </summary>
    /// <param name="name">The cursor name, for example <c>left_ptr</c>.</param>
    public void ShowNamed(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_showingName == name || _images?.Named(name, KeyAt(_x, _y)) is not { } image)
        {
            return;
        }

        _showingName = name;
        Show(image);
    }

    /// <summary>
    /// Sets the client cursor image. It shows only while the pointer is over a client.
    /// </summary>
    /// <param name="image">The image to show over the client.</param>
    public void ShowImage(CursorImage image)
    {
        _clientCursor = image;
        if (_overClient)
        {
            ApplyClientCursor();
        }
    }

    /// <summary>
    /// Clears the client cursor image. While the pointer is over a client, <c>left_ptr</c> shows.
    /// </summary>
    public void ClearClientCursor()
    {
        _clientCursor = null;
        if (_overClient)
        {
            ApplyClientCursor();
        }
    }

    /// <summary>
    /// Reports the surface under the pointer. A change of surface clears the client cursor image, and the client cursor shows only when <paramref name="overClient"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="surface">The surface under the pointer, or <see langword="null"/>.</param>
    /// <param name="overClient"><see langword="true"/> when the pointer is over client content rather than compositor chrome.</param>
    public void SetHover(Surface? surface, bool overClient)
    {
        if (!ReferenceEquals(surface, _hoverSurface))
        {
            _hoverSurface = surface;
            _clientCursor = null;
        }

        _overClient = overClient;
        if (overClient)
        {
            ApplyClientCursor();
        }
    }

    /// <summary>
    /// Takes a client <c>set_cursor</c> request. The controller follows commits on the cursor surface and sends its frame callbacks after the output shows the new image.
    /// </summary>
    /// <param name="request">The cursor surface and hotspot.</param>
    public void HandleCursorRequest(CursorRequest request)
    {
        if (!ReferenceEquals(request.Surface, _clientSurface))
        {
            if (_clientSurface is { } previous)
            {
                previous.Committed -= OnClientSurfaceCommitted;
                previous.Destroyed -= OnClientSurfaceGone;
            }

            _clientFramePending = false;
            _clientSurface = request.Surface;
            if (_clientSurface is { } current)
            {
                current.Committed += OnClientSurfaceCommitted;
                current.Destroyed += OnClientSurfaceGone;
            }
        }

        _clientHotspot = (request.HotspotX, request.HotspotY);
        TakeClientCursor();
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="Hide"/> hid the cursor.
    /// </summary>
    public bool IsHidden => _hidden;

    /// <summary>
    /// Hides the cursor on every output, or on the host in parent mode, until <see cref="Reveal"/>.
    /// </summary>
    public void Hide()
    {
        if (_hidden)
        {
            return;
        }

        _hidden = true;
        PublishCursor();
        if (_parentMode)
        {
            _parent?.HideCursor();
            return;
        }

        Withdraw();
    }

    /// <summary>
    /// Shows the cursor again after <see cref="Hide"/>.
    /// </summary>
    public void Reveal()
    {
        if (!_hidden)
        {
            return;
        }

        _hidden = false;
        if (_showing is not { } image)
        {
            return;
        }

        if (_parentMode)
        {
            _parent?.SetCursor(image.Buffer, image.HotspotX, image.HotspotY, _parentScale);
            PublishCursor();
            return;
        }

        Present(force: true);
    }

    private void Withdraw()
    {
        if (_cursorOn is { } output)
        {
            (output as IHardwareCursor)?.SetCursor(null, 0, 0);
            SceneOf(output)?.SetSoftwareCursor(null, 0, 0);
        }

        _cursorOn = null;
        _software = false;
    }

    /// <summary>
    /// Moves the cursor and moves it to another output when the position crosses one. A hidden cursor keeps the position and does not move.
    /// </summary>
    /// <param name="x">The X position in layout coordinates.</param>
    /// <param name="y">The Y position in layout coordinates.</param>
    public void MoveTo(double x, double y)
    {
        (_x, _y) = (x, y);
        if (_hidden)
        {
            return;
        }

        if (_parentMode)
        {
            if (_showing is { } image && Math.Abs(ScaleAt(x, y) - _parentScale) > 0.0001)
            {
                Show(image);
            }

            PublishCursor();
            return;
        }

        Present();
    }

    /// <summary>
    /// Stops following the layout, the outputs and the client cursor surface, and disposes the theme images and the allocator.
    /// </summary>
    public void Dispose()
    {
        _layout.Changed -= Refresh;
        foreach (var entry in _outputs)
        {
            entry.Output.Committed -= OnOutputCommitted;
            entry.Output.Frame -= OnOutputFrame;
        }

        _outputs.Clear();
        if (_clientSurface is { } surface)
        {
            surface.Committed -= OnClientSurfaceCommitted;
            surface.Destroyed -= OnClientSurfaceGone;
            _clientSurface = null;
        }

        _images?.Dispose();
        _images = null;
        _allocator?.Dispose();
        _allocator = null;
    }

    private void ApplyClientCursor(bool force = false)
    {
        if (_clientCursor is not { } image)
        {
            ShowNamed("left_ptr");
            return;
        }

        if (!force && _showingName.Length == 0 && _showing == image)
        {
            return;
        }

        _showingName = string.Empty;
        Show(image);
    }

    private static double ClientCursorDensity(Surface surface)
    {
        var logical = surface.Current.Width;
        var buffer = surface.Current.Buffer?.Width ?? 0;
        return logical > 0 && buffer > 0 ? (double)buffer / logical : 1.0;
    }

    private void OnClientSurfaceCommitted()
    {
        TakeClientCursor();
        if (!_parentMode && !_hidden && _overClient && _cursorOn is not null)
        {
            _clientFramePending = true;
            return;
        }

        _clientSurface?.SendFrameDone((uint)Environment.TickCount);
    }

    private void OnOutputFrame()
    {
        if (!_clientFramePending)
        {
            return;
        }

        _clientFramePending = false;
        _clientSurface?.SendFrameDone((uint)Environment.TickCount);
    }

    private void OnClientSurfaceGone()
    {
        _clientFramePending = false;
        _clientSurface = null;
        _clientCursor = null;
        if (_overClient)
        {
            ApplyClientCursor();
        }
    }

    private void TakeClientCursor()
    {
        var surface = _clientSurface;
        var density = surface is null ? 1 : ClientCursorDensity(surface);
        var scale = ScaleAt(_x, _y) / density;

        _clientCursor = surface?.Current.Buffer is { } source
            ? _images?.FromSurface(
                source,
                (int)Math.Round(_clientHotspot.X * density),
                (int)Math.Round(_clientHotspot.Y * density),
                KeyAt(_x, _y),
                scale)
            : null;

        if (_overClient)
        {
            ApplyClientCursor(force: true);
        }
    }

    private void Show(CursorImage image)
    {
        _showing = image;
        if (_hidden)
        {
            return;
        }

        if (_parentMode)
        {
            _parentScale = ScaleAt(_x, _y);
            _parent?.SetCursor(image.Buffer, image.HotspotX, image.HotspotY, _parentScale);
            PublishCursor();
            return;
        }

        Present(force: true);
    }

    private void Present(bool force = false)
    {
        if (_hidden || _showing is not { } image)
        {
            return;
        }

        var at = _layout.OutputAt(_x, _y);
        var index = -1;
        for (var i = 0; at is not null && i < _outputs.Count; i++)
        {
            if (ReferenceEquals(_outputs[i].Output, at))
            {
                index = i;
                break;
            }
        }

        if (_cursorOn is { } previous && !ReferenceEquals(previous, at))
        {
            (previous as IHardwareCursor)?.SetCursor(null, 0, 0);
            SceneOf(previous)?.SetSoftwareCursor(null, 0, 0);
        }

        if (index < 0)
        {
            _cursorOn = null;
            PublishCursor();
            return;
        }

        var (output, scene) = _outputs[index];
        if (_showingName.Length > 0 &&
            _images?.Named(_showingName, KeyFor(output)) is { } variant &&
            !variant.Equals(image))
        {
            image = variant;
            _showing = variant;
            force = true;
        }

        if (force || !ReferenceEquals(_cursorOn, output))
        {
            if (!image.Clipped &&
                output.Transform == OutputTransform.Normal &&
                output is IHardwareCursor plane &&
                plane.SetCursor(image.Buffer, image.HotspotX, image.HotspotY))
            {
                scene?.SetSoftwareCursor(null, 0, 0);
                _software = false;
            }
            else
            {
                if (!_software)
                {
                    (output as IHardwareCursor)?.SetCursor(null, 0, 0);
                }

                scene?.SetSoftwareCursor(image.Buffer, image.HotspotX, image.HotspotY);
                _software = true;
            }
        }

        _cursorOn = output;
        var box = _layout.BoxOf(output);

        var scale = output.Scale;
        var x = (int)((_x - box.X) * scale);
        var y = (int)((_y - box.Y) * scale);
        if (_software)
        {
            scene?.MoveSoftwareCursor(x, y);
        }
        else
        {
            (output as IHardwareCursor)?.MoveCursor(x, y);
            if (output is IHardwareCursor { CursorAwaitingFrame: true })
            {
                scene?.RequestPlaneCommit();
            }
        }

        PublishCursor();
    }

    private void PublishCursor()
    {
        if (Capture is not { } capture)
        {
            return;
        }

        if (_hidden || _showing is not { } image)
        {
            capture.SetCursor(null, default);
            return;
        }

        capture.SetCursor(
            image.Buffer,
            new CaptureCursorState(
                (int)Math.Round(_x),
                (int)Math.Round(_y),
                image.HotspotX,
                image.HotspotY,
                image.Width,
                image.Height,
                IsVisible: true));
    }

    private SceneOutput? SceneOf(IOutput output)
    {
        foreach (var entry in _outputs)
        {
            if (ReferenceEquals(entry.Output, output))
            {
                return entry.Scene;
            }
        }

        return null;
    }

    private double MaxScale()
    {
        var scale = 1.0;
        foreach (var entry in _outputs)
        {
            scale = Math.Max(scale, entry.Output.Scale);
        }

        return scale * _magnification;
    }

    private CursorKey KeyFor(IOutput output) =>
        new(output.Scale * _magnification, _descriptions.TryGetValue(output, out var description) ? description : null);

    private CursorKey KeyAt(double x, double y) =>
        DrivenAt(x, y) is { } output ? KeyFor(output)
        : _outputs.Count > 0 ? KeyFor(_outputs[0].Output)
        : new CursorKey(1, null);

    private double ScaleAt(double x, double y)
    {
        if (DrivenAt(x, y) is { } output)
        {
            return output.Scale * _magnification;
        }

        return (_outputs.Count > 0 ? _outputs[0].Output.Scale : 1.0) * _magnification;
    }

    private IOutput? DrivenAt(double x, double y)
    {
        if (_layout.OutputAt(x, y) is not { } output)
        {
            return null;
        }

        foreach (var entry in _outputs)
        {
            if (ReferenceEquals(entry.Output, output))
            {
                return output;
            }
        }

        return null;
    }
}
