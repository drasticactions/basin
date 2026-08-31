using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Seat;
using static Basin.Desktop.DesktopLog;

namespace Basin.Desktop;

public sealed class CursorController : IDisposable
{
    private readonly OutputLayout _layout;
    private readonly List<(IOutput Output, SceneOutput? Scene)> _outputs = [];
    private readonly Dictionary<IOutput, ImageDescription?> _descriptions = [];

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

    private bool _parentMode;
    private IParentCursor? _parent;
    private double _parentScale = 1;

    private IOutput? _cursorOn;
    private double _magnification = 1.0;
    private bool _software;
    private double _x, _y;

    public CursorController(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _layout.Changed += Refresh;
    }

    public IScreenCapture? Capture { get; set; }

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

    public CursorImages? Images => _images;

    public IOutput? CursorOutput => _cursorOn;

    public bool IsSoftwareOn(IOutput output) => ReferenceEquals(_cursorOn, output) && _software;

    public string DrawnBy =>
        _images?.Named("left_ptr") is null ? "none"
        : _parentMode ? "parent"
        : _software ? "software"
        : "plane";

    public string Showing =>
        _showing is null ? "nothing" : _showingName.Length > 0 ? _showingName : "client";

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

    public void AddOutput(IOutput output, SceneOutput? scene)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputs.Add((output, scene));
        output.Committed += OnOutputCommitted;
        Refresh();
    }

    public void RemoveOutput(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Committed -= OnOutputCommitted;
        _outputs.RemoveAll(entry => ReferenceEquals(entry.Output, output));
        if (ReferenceEquals(_cursorOn, output))
        {
            _cursorOn = null;
        }

        Refresh();
    }

    public CursorShapeManager? Shapes { get; set; }

    public IColorProfileService? ColorProfiles { get; set; }

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

    public void UseParentCursor() => _parentMode = true;

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

    public void ReloadForScale() =>
        _images?.ReloadForScale(MaxScale(), () =>
        {
            if (_showingName is { Length: > 0 } showing)
            {
                _showingName = string.Empty;
                ShowNamed(showing);
            }
        });

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

    public void ShowImage(CursorImage image)
    {
        _clientCursor = image;
        if (_overClient)
        {
            ApplyClientCursor();
        }
    }

    public void ClearClientCursor()
    {
        _clientCursor = null;
        if (_overClient)
        {
            ApplyClientCursor();
        }
    }

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

    public void HandleCursorRequest(CursorRequest request)
    {
        if (!ReferenceEquals(request.Surface, _clientSurface))
        {
            if (_clientSurface is { } previous)
            {
                previous.Committed -= OnClientSurfaceCommitted;
                previous.Destroyed -= OnClientSurfaceGone;
            }

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

    public bool IsHidden => _hidden;

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

    public void Dispose()
    {
        _layout.Changed -= Refresh;
        foreach (var entry in _outputs)
        {
            entry.Output.Committed -= OnOutputCommitted;
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

    private void OnClientSurfaceCommitted() => TakeClientCursor();

    private void OnClientSurfaceGone()
    {
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
