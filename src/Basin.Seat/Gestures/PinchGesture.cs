namespace Basin.Seat;

/// <summary>
/// Recognizes a two-finger pinch, as a member of the touch gesture chain. A third finger cancels a claimed pinch.
/// </summary>
public sealed class PinchGesture : ITouchGestures
{
    private const int Fingers = 2;

    private readonly Contact[] _contacts = new Contact[Fingers];
    private int _filled;
    private int _count;
    private Phase _phase;
    private double _startSpread;
    private double _startX;
    private double _startY;
    private double _scale = 1;
    private uint _beganMillis;
    private VelocityTracker _tracker;
    private long _pendingNanos;
    private bool _pending;
    private double _pendingScale;

    /// <summary>
    /// Gets or sets the change of the finger distance that starts the pinch. The change must also exceed the slide of the center.
    /// </summary>
    public double Slop { get; set; } = 24;

    /// <summary>
    /// Gets or sets the smallest distance between the fingers at the start for which a pinch can start.
    /// </summary>
    public double MinSpread { get; set; } = 8;

    /// <summary>
    /// Gets or sets the handler that accepts fingers and receives the pinch. With no handler, the gesture never claims.
    /// </summary>
    public IPinchHandler? Handler { get; set; }

    /// <summary>
    /// Gets a value indicating whether a pinch is claimed and not yet ended.
    /// </summary>
    public bool IsClaimed => _phase == Phase.Claimed;

    /// <summary>
    /// Gets the finger distance now divided by the distance at the start. It is 1 before a pinch starts.
    /// </summary>
    public double Scale => _scale;

    /// <inheritdoc/>
    public TouchGestureVerdict Down(int id, uint timeMs, double x, double y)
    {
        _count++;
        if (_phase == Phase.Claimed)
        {
            _phase = Phase.Spent;
            Handler?.Cancel();
            return TouchGestureVerdict.Owned;
        }

        if (_phase == Phase.Spent)
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
            if (_filled == Fingers && (_contacts[0].Accepted || _contacts[1].Accepted))
            {
                _startSpread = Spread();
                (_startX, _startY) = Centroid();
                if (_startSpread >= MinSpread)
                {
                    _phase = Phase.Watching;
                    _scale = 1;
                    _beganMillis = timeMs;
                    _tracker.Reset();
                    _pending = false;
                    Sample(0);
                }
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
                var spread = Spread();
                _scale = spread / _startSpread;
                Sample(Nanos(timeMs));
                var change = Math.Abs(spread - _startSpread);
                var (cx, cy) = Centroid();
                var slide = Math.Sqrt(((cx - _startX) * (cx - _startX)) + ((cy - _startY) * (cy - _startY)));
                if (change < Slop || change < slide)
                {
                    return TouchGestureVerdict.Pass;
                }

                if (Handler is { } handler && handler.Begin(_startX, _startY, _startSpread, timeMs))
                {
                    _phase = Phase.Claimed;
                    handler.Update(_scale, cx, cy, timeMs);
                    return TouchGestureVerdict.Claim;
                }

                _phase = Phase.Idle;
                return TouchGestureVerdict.Pass;

            case Phase.Claimed:
                if (tracked)
                {
                    _scale = Spread() / _startSpread;
                    Sample(Nanos(timeMs));
                    var (ux, uy) = Centroid();
                    Handler?.Update(_scale, ux, uy, timeMs);
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
                Handler?.End(_tracker.Velocity(Nanos(timeMs)).X, timeMs);
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
            Handler?.Cancel();
        }

        _phase = Phase.Idle;
        _count = 0;
        _filled = 0;
        _scale = 1;
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
        _pendingScale = _scale;
        _pending = true;
    }

    private void Flush()
    {
        if (_pending)
        {
            _tracker.Add(_pendingScale, 0, _pendingNanos);
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

    private double Spread()
    {
        var dx = _contacts[1].X - _contacts[0].X;
        var dy = _contacts[1].Y - _contacts[0].Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private (double X, double Y) Centroid() =>
        ((_contacts[0].X + _contacts[1].X) / 2, (_contacts[0].Y + _contacts[1].Y) / 2);

    private long Nanos(uint timeMs) => (long)(uint)(timeMs - _beganMillis) * 1_000_000;

    private readonly record struct Contact(int Id, double X, double Y, bool Accepted);

    private enum Phase
    {
        Idle,

        Watching,

        Claimed,

        Spent,
    }
}
