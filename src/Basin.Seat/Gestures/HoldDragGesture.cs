namespace Basin.Seat;

/// <summary>
/// Recognizes fingers that hold still and then drag, as a member of the touch gesture chain.
/// </summary>
public sealed class HoldDragGesture : ITouchGestures
{
    /// <summary>
    /// The largest value that <see cref="Fingers"/> accepts.
    /// </summary>
    public const int MaxFingers = 5;

    private readonly Contact[] _contacts = new Contact[MaxFingers];
    private readonly double[] _angles = new double[MaxFingers];
    private int _filled;
    private int _count;
    private Phase _phase;
    private uint _heldSince;
    private bool _begun;
    private double _turned;
    private uint _claimedAt;
    private VelocityTracker _tracker;
    private long _pendingNanos;
    private bool _pending;
    private double _pendingTurned;

    /// <summary>
    /// Gets or sets the number of fingers that the gesture needs. The value is clamped to the range 1 to <see cref="MaxFingers"/>.
    /// </summary>
    public int Fingers
    {
        get;
        set => field = Math.Clamp(value, 1, MaxFingers);
    } = 2;

    /// <summary>
    /// Gets or sets the time in milliseconds that the fingers must hold before a drag can start.
    /// </summary>
    public uint HoldMillis { get; set; } = 300;

    /// <summary>
    /// Gets or sets how far one finger can move during the hold before the gesture stops watching.
    /// </summary>
    public double StillSlop { get; set; } = 12;

    /// <summary>
    /// Gets or sets how far the center of the fingers must move after the hold to claim the drag.
    /// </summary>
    public double MoveSlop { get; set; } = 8;

    /// <summary>
    /// Gets or sets the handler that accepts fingers and receives the drag. With no handler, no finger is accepted.
    /// </summary>
    public IHoldDragHandler? Handler { get; set; }

    /// <summary>
    /// Gets a value indicating whether the gesture holds the contacts as a drag. It is true from the claim, before the handler accepts.
    /// </summary>
    public bool IsClaimed => _phase == Phase.Claimed;

    /// <inheritdoc/>
    public TouchGestureVerdict Down(int id, uint timeMs, double x, double y)
    {
        _count++;
        if (_phase is Phase.Claimed or Phase.Spent)
        {
            return TouchGestureVerdict.Owned;
        }

        if (_count == 1)
        {
            _filled = 0;
            _phase = Phase.Idle;
        }

        if (_phase == Phase.Idle && _filled >= 0 && _filled == _count - 1 && _filled < Fingers && Handler?.Accepts(x, y) == true)
        {
            _contacts[_filled++] = new Contact(id, x, y);
            if (_filled == Fingers)
            {
                _phase = Phase.Holding;
                _heldSince = timeMs;
                CaptureAngles();
            }

            return TouchGestureVerdict.Pass;
        }

        _filled = -1;
        _phase = Phase.Idle;
        return TouchGestureVerdict.Pass;
    }

    /// <inheritdoc/>
    public TouchGestureVerdict Motion(int id, uint timeMs, double x, double y)
    {
        var index = IndexOf(id);
        if (index >= 0)
        {
            _contacts[index] = _contacts[index] with { X = x, Y = y };
        }

        switch (_phase)
        {
            case Phase.Holding when index >= 0:
                if ((uint)(timeMs - _heldSince) < HoldMillis)
                {
                    if (Wandered(index))
                    {
                        _phase = Phase.Idle;
                    }

                    return TouchGestureVerdict.Pass;
                }

                var (startX, startY) = Centroid(start: true);
                var (nowX, nowY) = Centroid(start: false);
                if (Distance(startX, startY, nowX, nowY) < MoveSlop)
                {
                    return TouchGestureVerdict.Pass;
                }

                _phase = Phase.Claimed;
                _begun = false;
                _turned = 0;
                _claimedAt = timeMs;
                _tracker.Reset();
                _pending = false;
                Sample(0);
                return TouchGestureVerdict.Claim;

            case Phase.Claimed:
                if (index >= 0 && Handler is { } owner)
                {
                    if (!_begun)
                    {
                        var (fromX, fromY) = Centroid(start: true);
                        _begun = owner.Begin(fromX, fromY, timeMs);
                        if (!_begun)
                        {
                            _phase = Phase.Spent;
                            return TouchGestureVerdict.Owned;
                        }
                    }

                    var turn = Turn();
                    _turned += turn;
                    Sample((long)(uint)(timeMs - _claimedAt) * 1_000_000);
                    var (x2, y2) = Centroid(start: false);
                    owner.Update(x2, y2, turn, timeMs);
                }

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
        _count = Math.Max(0, _count - 1);
        var tracked = IndexOf(id) >= 0;
        switch (_phase)
        {
            case Phase.Claimed:
                if (_begun)
                {
                    Flush();
                    Handler?.End(cancelled: false, _tracker.Velocity((long)(uint)(timeMs - _claimedAt) * 1_000_000).X, timeMs);
                }

                _phase = _count > 0 ? Phase.Spent : Phase.Idle;
                return TouchGestureVerdict.Finish;

            case Phase.Spent:
                if (_count == 0)
                {
                    _phase = Phase.Idle;
                }

                return TouchGestureVerdict.Owned;

            default:
                if (tracked)
                {
                    _phase = Phase.Idle;
                    _filled = -1;
                }

                return TouchGestureVerdict.Pass;
        }
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        if (_phase == Phase.Claimed && _begun)
        {
            Handler?.End(cancelled: true, 0, 0);
        }

        _phase = Phase.Idle;
        _count = 0;
        _filled = 0;
    }

    /// <summary>
    /// Returns zero. This gesture never withholds events.
    /// </summary>
    /// <param name="into">The buffer, which is not written.</param>
    /// <returns>Always zero.</returns>
    public int TakeWithheld(Span<EdgeSwipeSample> into) => 0;

    private int IndexOf(int id)
    {
        for (var i = 0; i < Math.Max(_filled, 0); i++)
        {
            if (_contacts[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void CaptureAngles()
    {
        var (cx, cy) = Centroid(start: false);
        for (var i = 0; i < _filled; i++)
        {
            _angles[i] = Math.Atan2(_contacts[i].Y - cy, _contacts[i].X - cx);
        }
    }

    private double Turn()
    {
        if (_filled < 2)
        {
            return 0;
        }

        var (cx, cy) = Centroid(start: false);
        var turn = 0.0;
        for (var i = 0; i < _filled; i++)
        {
            var angle = Math.Atan2(_contacts[i].Y - cy, _contacts[i].X - cx);
            turn += Math.IEEERemainder(angle - _angles[i], Math.Tau);
            _angles[i] = angle;
        }

        return turn / _filled;
    }

    private void Sample(long nanos)
    {
        if (_pending && nanos != _pendingNanos)
        {
            Flush();
        }

        _pendingNanos = nanos;
        _pendingTurned = _turned;
        _pending = true;
    }

    private void Flush()
    {
        if (_pending)
        {
            _tracker.Add(_pendingTurned, 0, _pendingNanos);
            _pending = false;
        }
    }

    private bool Wandered(int index) =>
        Distance(_contacts[index].DownX, _contacts[index].DownY, _contacts[index].X, _contacts[index].Y) > StillSlop;

    private (double X, double Y) Centroid(bool start)
    {
        double x = 0, y = 0;
        for (var i = 0; i < _filled; i++)
        {
            x += start ? _contacts[i].DownX : _contacts[i].X;
            y += start ? _contacts[i].DownY : _contacts[i].Y;
        }

        return (x / _filled, y / _filled);
    }

    private static double Distance(double ax, double ay, double bx, double by) =>
        Math.Sqrt(((bx - ax) * (bx - ax)) + ((by - ay) * (by - ay)));

    private readonly record struct Contact(int Id, double X, double Y)
    {
        public double DownX { get; init; } = X;

        public double DownY { get; init; } = Y;
    }

    private enum Phase
    {
        Idle,

        Holding,

        Claimed,

        Spent,
    }
}
