namespace Basin.Seat;

public sealed class CentroidSwipeGesture : ITouchGestures
{
    private readonly TouchContacts _contacts = new();
    private Phase _phase;
    private double _travel;

    public uint Fingers { get; set; } = 3;

    public double Slop { get; set; } = 24;

    public ICentroidSwipeHandler? Handler { get; set; }

    public bool IsClaimed => _phase == Phase.Claimed;

    public TouchGestureVerdict Down(int id, uint timeMs, double x, double y)
    {
        _contacts.Down(id, x, y);
        if (_phase is Phase.Claimed or Phase.Spent)
        {
            return TouchGestureVerdict.Owned;
        }

        if (_phase == Phase.Idle && _contacts.Count == (int)Fingers)
        {
            _phase = Phase.Watching;
            _travel = 0;
        }

        return TouchGestureVerdict.Pass;
    }

    public TouchGestureVerdict Motion(int id, uint timeMs, double x, double y)
    {
        if (!_contacts.Motion(id, x, y, out var dx, out var dy))
        {
            return _phase is Phase.Claimed or Phase.Spent
                ? TouchGestureVerdict.Owned
                : TouchGestureVerdict.Pass;
        }

        switch (_phase)
        {
            case Phase.Watching:
                _travel += dx;
                if (Math.Abs(_travel) < Slop)
                {
                    return TouchGestureVerdict.Pass;
                }

                if (Handler is { } handler &&
                    _contacts.TryCentroid(out var centerX, out var centerY) &&
                    handler.Begin(centerX, centerY, timeMs))
                {
                    _phase = Phase.Claimed;
                    return TouchGestureVerdict.Claim;
                }

                _phase = Phase.Idle;
                return TouchGestureVerdict.Pass;

            case Phase.Claimed:
                Handler?.Update(dx, dy, timeMs);
                return TouchGestureVerdict.Owned;

            case Phase.Spent:
                return TouchGestureVerdict.Owned;

            default:
                return TouchGestureVerdict.Pass;
        }
    }

    public TouchGestureVerdict Up(int id, uint timeMs)
    {
        _ = _contacts.Up(id);
        switch (_phase)
        {
            case Phase.Watching when _contacts.Count < (int)Fingers:
                _phase = Phase.Idle;
                return TouchGestureVerdict.Pass;

            case Phase.Claimed:
                Handler?.End(cancelled: false, timeMs);
                _phase = _contacts.Count > 0 ? Phase.Spent : Phase.Idle;
                return TouchGestureVerdict.Finish;

            case Phase.Spent:
                if (_contacts.Count == 0)
                {
                    _phase = Phase.Idle;
                }

                return TouchGestureVerdict.Owned;

            default:
                return TouchGestureVerdict.Pass;
        }
    }

    public void Cancel()
    {
        _contacts.Clear();
        if (_phase == Phase.Claimed)
        {
            Handler?.End(cancelled: true, timeMs: 0);
        }

        _phase = Phase.Idle;
        _travel = 0;
    }

    public int TakeWithheld(Span<EdgeSwipeSample> into) => 0;

    private enum Phase
    {
        Idle,

        Watching,

        Claimed,

        Spent,
    }
}
