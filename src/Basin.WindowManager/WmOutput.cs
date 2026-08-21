using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

public sealed class WmOutput
{
    private readonly RiverWindowManager _wm;
    private readonly RiverOutputV1 _proxy;

    private Point _pendingPosition;
    private bool _positionChanged;
    private Size _pendingDimensions;
    private bool _dimensionsChanged;
    private uint _pendingCaptureSessions;
    private bool _captureSessionsChanged;
    private Rect _pendingNonExclusiveArea;
    private bool _nonExclusiveAreaChanged;
    private bool _removedPending;

    internal WmOutput(RiverWindowManager wm, RiverOutputV1 proxy)
    {
        _wm = wm;
        _proxy = proxy;

        proxy.WlOutput += (_, e) => WlOutputName = e.Name;
        proxy.Position += (_, e) =>
        {
            _pendingPosition = new Point(e.X, e.Y);
            _positionChanged = true;
        };
        proxy.Dimensions += (_, e) =>
        {
            _pendingDimensions = new Size(e.Width, e.Height);
            _dimensionsChanged = true;
        };
        proxy.CaptureSessions += (_, e) =>
        {
            _pendingCaptureSessions = e.Count;
            _captureSessionsChanged = true;
        };
        proxy.Removed += (_, _) =>
        {
            IsRemoved = true;
            _removedPending = true;
            _wm.OnOutputRemoved(this);
        };
    }

    public Point Position { get; private set; }

    public Size Dimensions { get; private set; }

    public Rect Area => new(Position.X, Position.Y, Dimensions.Width, Dimensions.Height);

    public uint WlOutputName { get; private set; }

    public int CaptureSessions { get; private set; }

    public Rect NonExclusiveArea { get; private set; }

    public bool IsRemoved { get; private set; }

    public event Action? Removed;

    public void SetPresentationMode(PresentationMode mode)
    {
        _wm.EnsureRender(nameof(SetPresentationMode));
        _wm.RequireVersion(4, "set_presentation_mode");
        _proxy.SetPresentationMode((RiverOutputV1.PresentationMode)mode);
    }

    public void SetDefaultForLayerSurfaces()
    {
        _wm.EnsureManage(nameof(SetDefaultForLayerSurfaces));
        if (_wm.LayerShell is null)
        {
            throw new NotSupportedException(
                "the compositor does not offer river_layer_shell_v1, so layer surfaces have no default output");
        }

        _wm.LayerShell.OutputStateFor(this).SetDefault();
    }

    public override string ToString() =>
        $"output {Dimensions.Width}x{Dimensions.Height} at {Position.X},{Position.Y}";

    internal RiverOutputV1 Proxy => _proxy;

    internal void ReportNonExclusiveArea(Rect area)
    {
        _pendingNonExclusiveArea = area;
        _nonExclusiveAreaChanged = true;
    }

    internal void ApplyPending()
    {
        if (_positionChanged)
        {
            (Position, _positionChanged) = (_pendingPosition, false);
        }

        if (_dimensionsChanged)
        {
            (Dimensions, _dimensionsChanged) = (_pendingDimensions, false);
        }

        if (_captureSessionsChanged)
        {
            CaptureSessions = (int)_pendingCaptureSessions;
            _captureSessionsChanged = false;
        }

        if (_nonExclusiveAreaChanged)
        {
            (NonExclusiveArea, _nonExclusiveAreaChanged) = (_pendingNonExclusiveArea, false);
        }
    }

    internal void FirePending()
    {
        if (!_removedPending)
        {
            return;
        }

        _removedPending = false;
        Removed?.Invoke();
    }

    internal void DestroyProxy()
    {
        if (!_proxy.IsDestroyed)
        {
            _proxy.Destroy();
        }
    }
}
