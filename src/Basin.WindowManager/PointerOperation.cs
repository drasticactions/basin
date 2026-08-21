namespace Basin.WindowManager;

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

    public WmSeat Seat => _seat;

    public Point Delta { get; private set; }

    public bool IsReleased { get; private set; }

    public bool IsEnded { get; private set; }

    public event Action? Changed;

    public event Action? Released;

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
