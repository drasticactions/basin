using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// An area of the compositor's logical space that the manager treats as one output. Output areas do not overlap when a manage sequence starts.
/// </summary>
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

    /// <summary>
    /// Gets the top-left corner in the compositor's logical space, as of the start of the current sequence.
    /// </summary>
    public Point Position { get; private set; }

    /// <summary>
    /// Gets the size in logical pixels, as of the start of the current sequence.
    /// </summary>
    public Size Dimensions { get; private set; }

    /// <summary>
    /// Gets the output's rectangle in the compositor's logical space.
    /// </summary>
    public Rect Area => new(Position.X, Position.Y, Dimensions.Width, Dimensions.Height);

    /// <summary>
    /// Gets the registry name of the matching <c>wl_output</c> global. The mapping never changes, but that global can go away before <see cref="Removed"/> fires.
    /// </summary>
    public uint WlOutputName { get; private set; }

    /// <summary>
    /// Gets the number of screen capture sessions that record this output, as of the start of the current sequence.
    /// </summary>
    public int CaptureSessions { get; private set; }

    /// <summary>
    /// Gets the area left after layer surfaces take their exclusive zones, in global coordinates. It stays empty until <see cref="WmLayerShell.Track(WmOutput)"/> or <see cref="SetDefaultForLayerSurfaces"/> binds the output's layer state.
    /// </summary>
    public Rect NonExclusiveArea { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the compositor removed the output. It becomes true when the event arrives, before <see cref="Removed"/> fires.
    /// </summary>
    public bool IsRemoved { get; private set; }

    /// <summary>
    /// Occurs once at the start of the next sequence after the compositor removes the output. The library destroys the output when that manage sequence ends.
    /// </summary>
    public event Action? Removed;

    /// <summary>
    /// Sets how the output presents frames. The compositor uses <see cref="PresentationMode.Vsync"/> until the manager asks for another mode.
    /// </summary>
    /// <param name="mode">The preferred presentation mode.</param>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">The bound protocol version is lower than 4.</exception>
    public void SetPresentationMode(PresentationMode mode)
    {
        _wm.EnsureRender(nameof(SetPresentationMode));
        _wm.RequireVersion(4, "set_presentation_mode");
        _proxy.SetPresentationMode((RiverOutputV1.PresentationMode)mode);
    }

    /// <summary>
    /// Makes this output the default for new layer surfaces that name no output. It replaces any earlier default.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">The compositor does not offer the river layer shell.</exception>
    public void SetDefaultForLayerSurfaces()
    {
        _wm.EnsureManage(nameof(SetDefaultForLayerSurfaces));
        if (_wm.LayerShell is null)
        {
            throw new NotSupportedException(
                "The compositor does not offer river_layer_shell_v1, so layer surfaces have no default output.");
        }

        _wm.LayerShell.OutputStateFor(this).SetDefault();
    }

    /// <inheritdoc/>
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
