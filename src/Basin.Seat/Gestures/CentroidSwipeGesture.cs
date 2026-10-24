namespace Basin.Seat;

/// <summary>
/// Recognizes a swipe of a fixed number of fingers by the motion of their center, as a member of the touch gesture chain.
/// </summary>
public sealed class CentroidSwipeGesture : ITouchGestures
{
    private readonly TouchContacts _contacts = new();
    private Phase _phase;
    private double _travel;

    /// <summary>
    /// Gets or sets the exact number of contacts that the swipe needs. One more contact stops the watch.
    /// </summary>
    public uint Fingers { get; set; } = 3;

    /// <summary>
    /// Gets or sets the travel of the center along <see cref="Axis"/> that starts the swipe.
    /// </summary>
    public double Slop { get; set; } = 24;

    /// <summary>
    /// Gets or sets the axis along which the slop is measured.
    /// </summary>
    public SwipeAxis Axis { get; set; } = SwipeAxis.Horizontal;

    /// <summary>
    /// Gets or sets the handler that decides to claim and receives the swipe. With no handler, the gesture never claims.
    /// </summary>
    public ICentroidSwipeHandler? Handler { get; set; }

    /// <summary>
    /// Gets a value indicating whether a swipe is claimed and not yet ended.
    /// </summary>
    public bool IsClaimed => _phase == Phase.Claimed;

    /// <inheritdoc/>
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
        else if (_phase == Phase.Watching && _contacts.Count > (int)Fingers)
        {
            _phase = Phase.Idle;
        }

        return TouchGestureVerdict.Pass;
    }

    /// <inheritdoc/>
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
                _travel += Axis == SwipeAxis.Vertical ? dy : dx;
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <summary>
    /// Returns zero. This gesture never withholds events.
    /// </summary>
    /// <param name="into">The buffer, which is not written.</param>
    /// <returns>Always zero.</returns>
    public int TakeWithheld(Span<EdgeSwipeSample> into) => 0;

    private enum Phase
    {
        Idle,

        Watching,

        Claimed,

        Spent,
    }
}
