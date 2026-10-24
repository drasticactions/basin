namespace Basin.WindowManager;

/// <summary>
/// An interactive pointer operation on a seat, for a move or a resize. It continues until <see cref="End"/>, also after the buttons come up.
/// </summary>
public sealed class PointerOperation
{
    private readonly RiverWindowManager _wm;
    private readonly WmSeat _seat;
    private Point _pendingDelta;
    private bool _deltaChanged;
    private bool _releasePending;

    internal PointerOperation(RiverWindowManager wm, WmSeat seat)
    {
        _wm = wm;
        _seat = seat;
    }

    /// <summary>
    /// Gets the seat that drives the operation.
    /// </summary>
    public WmSeat Seat => _seat;

    /// <summary>
    /// Gets the total pointer motion since the operation started, not the change since the last event. Add it to the geometry recorded at the start.
    /// </summary>
    public Point Delta { get; private set; }

    /// <summary>
    /// Gets a value indicating whether all pointer buttons came up. Deltas can still arrive until <see cref="End"/>.
    /// </summary>
    public bool IsReleased { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="End"/> ran.
    /// </summary>
    public bool IsEnded { get; private set; }

    /// <summary>
    /// Occurs at the start of a sequence when <see cref="Delta"/> changed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Occurs once at the start of a sequence after all pointer buttons came up.
    /// </summary>
    public event Action? Released;

    /// <summary>
    /// Ends the operation. The compositor ends it when this manage sequence finishes. A second call does nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void End()
    {
        _wm.EnsureManage(nameof(End));
        if (IsEnded)
        {
            return;
        }

        IsEnded = true;
        _seat.EndOperation(this);
    }

    internal void ReportDelta(Point delta)
    {
        _pendingDelta = delta;
        _deltaChanged = true;
    }

    internal void ReportReleased() => _releasePending = true;

    internal void FirePending()
    {
        if (_deltaChanged)
        {
            (Delta, _deltaChanged) = (_pendingDelta, false);
            Changed?.Invoke();
        }

        if (_releasePending)
        {
            _releasePending = false;
            IsReleased = true;
            Released?.Invoke();
        }
    }
}
