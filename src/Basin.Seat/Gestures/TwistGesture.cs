namespace Basin.Seat;

/// <summary>
/// Recognizes a rotation of two or more fingers around their center, as a member of the touch gesture chain.
/// </summary>
public sealed class TwistGesture : ITouchGestures
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
    private double _total;
    private double _startX;
    private double _startY;
    private uint _beganMillis;
    private VelocityTracker _tracker;
    private long _pendingNanos;
    private bool _pending;
    private double _pendingTotal;

    /// <summary>
    /// Gets or sets the turn in radians that starts the twist. The arc of the turn must also exceed the slide of the center.
    /// </summary>
    public double Slop { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets the number of fingers that the twist needs. The value is clamped to the range 2 to <see cref="MaxFingers"/>.
    /// </summary>
    public int Fingers
    {
        get;
        set => field = Math.Clamp(value, 2, MaxFingers);
    } = 2;

    /// <summary>
    /// Gets or sets the handler that accepts fingers and receives the twist. With no handler, the gesture never claims.
    /// </summary>
    public ITwistHandler? Handler { get; set; }

    /// <summary>
    /// Gets a value indicating whether a twist is claimed and not yet ended.
    /// </summary>
    public bool IsClaimed => _phase == Phase.Claimed;

    /// <summary>
    /// Gets the total turn in radians since the fingers landed. A positive value is clockwise on a screen with y down.
    /// </summary>
    public double Angle => _total;

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

        if (_phase == Phase.Idle && _filled >= 0 && _filled == _count - 1 && _filled < Fingers && IndexOf(id) < 0)
        {
            _contacts[_filled++] = new Contact(id, x, y, Handler?.Accepts(x, y) == true);
            if (_filled == Fingers && AnyAccepted())
            {
                _phase = Phase.Watching;
                (_startX, _startY) = Centroid();
                CaptureAngles();
                _total = 0;
                _beganMillis = timeMs;
                _tracker.Reset();
                _pending = false;
                Sample(0);
            }

            return TouchGestureVerdict.Pass;
        }

        _filled = -1;
        if (_phase == Phase.Watching)
        {
            _phase = Phase.Idle;
        }

        return TouchGestureVerdict.Pass;
    }

    /// <inheritdoc/>
    public TouchGestureVerdict Motion(int id, uint timeMs, double x, double y)
    {
        var tracked = Move(id, x, y);
        switch (_phase)
        {
            case Phase.Watching when tracked:
                _total += Turn();
                Sample(Nanos(timeMs));
                if (Math.Abs(_total) < Slop || !TurnDominates())
                {
                    return TouchGestureVerdict.Pass;
                }

                var anchor = FirstAccepted();
                if (Handler is { } handler && handler.Begin(anchor.DownX, anchor.DownY, timeMs))
                {
                    _phase = Phase.Claimed;
                    handler.Update(_total, timeMs);
                    return TouchGestureVerdict.Claim;
                }

                _phase = Phase.Idle;
                return TouchGestureVerdict.Pass;

            case Phase.Claimed:
                if (tracked)
                {
                    var turn = Turn();
                    _total += turn;
                    Sample(Nanos(timeMs));
                    Handler?.Update(turn, timeMs);
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
                Flush();
                Handler?.End(cancelled: false, _tracker.Velocity(Nanos(timeMs)).X, timeMs);
                _phase = _count > 0 ? Phase.Spent : Phase.Idle;
                return TouchGestureVerdict.Finish;

            case Phase.Spent:
                if (_count == 0)
                {
                    _phase = Phase.Idle;
                }

                return TouchGestureVerdict.Owned;

            case Phase.Watching when tracked:
                _phase = Phase.Idle;
                _filled = -1;
                return TouchGestureVerdict.Pass;

            default:
                if (tracked)
                {
                    _filled = -1;
                }

                return TouchGestureVerdict.Pass;
        }
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        if (_phase == Phase.Claimed)
        {
            Handler?.End(cancelled: true, 0, 0);
        }

        _phase = Phase.Idle;
        _count = 0;
        _filled = 0;
        _total = 0;
        _tracker.Reset();
    }

    /// <summary>
    /// Returns zero. This gesture never withholds events.
    /// </summary>
    /// <param name="into">The buffer, which is not written.</param>
    /// <returns>Always zero.</returns>
    public int TakeWithheld(Span<EdgeSwipeSample> into) => 0;

    private void Sample(long nanos)
    {
        if (_pending && nanos != _pendingNanos)
        {
            Flush();
        }

        _pendingNanos = nanos;
        _pendingTotal = _total;
        _pending = true;
    }

    private void Flush()
    {
        if (_pending)
        {
            _tracker.Add(_pendingTotal, 0, _pendingNanos);
            _pending = false;
        }
    }

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

    private bool Move(int id, double x, double y)
    {
        var index = IndexOf(id);
        if (index < 0)
        {
            return false;
        }

        _contacts[index] = _contacts[index] with { X = x, Y = y };
        return true;
    }

    private bool AnyAccepted()
    {
        for (var i = 0; i < _filled; i++)
        {
            if (_contacts[i].Accepted)
            {
                return true;
            }
        }

        return false;
    }

    private Contact FirstAccepted()
    {
        for (var i = 0; i < _filled; i++)
        {
            if (_contacts[i].Accepted)
            {
                return _contacts[i];
            }
        }

        return _contacts[0];
    }

    private (double X, double Y) Centroid()
    {
        double x = 0, y = 0;
        for (var i = 0; i < _filled; i++)
        {
            x += _contacts[i].X;
            y += _contacts[i].Y;
        }

        return (x / _filled, y / _filled);
    }

    private void CaptureAngles()
    {
        var (cx, cy) = Centroid();
        for (var i = 0; i < _filled; i++)
        {
            _angles[i] = Math.Atan2(_contacts[i].Y - cy, _contacts[i].X - cx);
        }
    }

    private double Turn()
    {
        var (cx, cy) = Centroid();
        var turn = 0.0;
        for (var i = 0; i < _filled; i++)
        {
            var angle = Math.Atan2(_contacts[i].Y - cy, _contacts[i].X - cx);
            turn += Math.IEEERemainder(angle - _angles[i], Math.Tau);
            _angles[i] = angle;
        }

        return turn / _filled;
    }

    private bool TurnDominates()
    {
        var (cx, cy) = Centroid();
        var radius = 0.0;
        for (var i = 0; i < _filled; i++)
        {
            radius += Math.Sqrt(((_contacts[i].X - cx) * (_contacts[i].X - cx)) + ((_contacts[i].Y - cy) * (_contacts[i].Y - cy)));
        }

        var arc = Math.Abs(_total) * radius / _filled;
        var slide = Math.Sqrt(((cx - _startX) * (cx - _startX)) + ((cy - _startY) * (cy - _startY)));
        return arc >= slide;
    }

    private long Nanos(uint timeMs) => (long)(uint)(timeMs - _beganMillis) * 1_000_000;

    private readonly record struct Contact(int Id, double X, double Y, bool Accepted)
    {
        public double DownX { get; init; } = X;

        public double DownY { get; init; } = Y;
    }

    private enum Phase
    {
        Idle,

        Watching,

        Claimed,

        Spent,
    }
}
