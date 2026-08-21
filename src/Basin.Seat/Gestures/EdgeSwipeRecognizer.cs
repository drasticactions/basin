namespace Basin.Seat;

public sealed class EdgeSwipeRecognizer
{
    public const int WithheldCapacity = 32;

    private const double VelocitySmoothing = 0.4;
    private const uint StaleVelocityMillis = 60;

    private readonly EdgeSwipeSample[] _withheld = new EdgeSwipeSample[WithheldCapacity];
    private int _withheldCount;
    private int _id;
    private Phase _phase;
    private ScreenEdge _edge;
    private double _width;
    private double _height;
    private double _originIn;
    private double _originAlong;
    private double _in;
    private double _releaseX;
    private double _releaseY;
    private double _velocity;
    private bool _hasVelocity;
    private uint _beganMillis;
    private uint _lastMillis;
    private uint _holdSince;
    private bool _held;
    private bool _reachedIn;
    private bool _backedOut;

    public double BandWidth { get; set; } = 20;

    public double Scale { get; set; } = 1;

    public ScreenEdges Edges { get; set; } = ScreenEdges.All;

    public double ClaimDistance { get; set; } = 8;

    public double RevealDistance { get; set; } = 150;

    public double AlongSlop { get; set; } = 16;

    public uint WithholdMillis { get; set; } = 100;

    public double CommitFraction { get; set; } = 0.5;

    public double ReturnFraction { get; set; } = 0.15;

    public double HoldFraction { get; set; } = 0.33;

    public double HoldTolerance { get; set; } = 0.12;

    public uint HoldMillis { get; set; } = 400;

    public double FlingPerSecond { get; set; } = 1200;

    public double SideZoneFraction { get; set; } = 0.25;

    public double BottomZoneFraction { get; set; } = 0.75;

    public ScreenEdge Edge => _edge;

    public bool IsCandidate => _phase == Phase.Candidate;

    public bool IsClaimed => _phase == Phase.Claimed;

    public int ContactId => _id;

    public double Progress
    {
        get
        {
            var reveal = RevealDistance * Scale;
            if (reveal <= 0)
            {
                return 0;
            }

            var progress = _in / reveal;
            return progress <= 0 ? 0 : progress >= 1 ? 1 : progress;
        }
    }

    public double Velocity => _velocity;

    public EdgeSwipeOutcome Outcome { get; private set; }

    public EdgeSwipeZone Zone { get; private set; }

    public EdgeSwipeAction Begin(int id, double x, double y, double width, double height, uint timeMs)
    {
        if (_phase != Phase.Idle || width <= 0 || height <= 0)
        {
            return EdgeSwipeAction.Ignore;
        }

        var edge = EdgeAt(x, y, width, height);
        if (edge == ScreenEdge.None)
        {
            return EdgeSwipeAction.Ignore;
        }

        _phase = Phase.Candidate;
        _id = id;
        _edge = edge;
        _width = width;
        _height = height;
        Outcome = EdgeSwipeOutcome.None;
        Zone = EdgeSwipeZone.None;
        _velocity = 0;
        _hasVelocity = false;
        _held = false;
        _reachedIn = false;
        _backedOut = false;
        _holdSince = 0;
        _beganMillis = timeMs;
        _lastMillis = timeMs;
        Project(x, y, out _originIn, out _originAlong);
        _in = _originIn;
        _releaseX = x;
        _releaseY = y;
        _withheldCount = 0;
        Withhold(new EdgeSwipeSample(timeMs, x, y, Down: true));
        return EdgeSwipeAction.Withhold;
    }

    public EdgeSwipeAction Update(int id, double x, double y, uint timeMs)
    {
        if (_phase == Phase.Idle || id != _id)
        {
            return EdgeSwipeAction.Ignore;
        }

        Project(x, y, out var inward, out var along);
        _releaseX = x;
        _releaseY = y;

        if (_phase == Phase.Candidate)
        {
            var travel = inward - _originIn;
            var drift = Math.Abs(along - _originAlong);
            if (drift > AlongSlop * Scale && drift > travel)
            {
                return Decline();
            }

            if (travel >= ClaimDistance * Scale)
            {
                _phase = Phase.Claimed;
                _withheldCount = 0;
                Track(inward, timeMs);
                return EdgeSwipeAction.Claim;
            }

            if (timeMs - _beganMillis > WithholdMillis || _withheldCount >= WithheldCapacity)
            {
                return Decline();
            }

            Withhold(new EdgeSwipeSample(timeMs, x, y, Down: false));
            return EdgeSwipeAction.Withhold;
        }

        Track(inward, timeMs);
        return EdgeSwipeAction.Track;
    }

    public EdgeSwipeAction End(int id, uint timeMs, bool cancelled = false)
    {
        if (_phase == Phase.Idle || id != _id)
        {
            return EdgeSwipeAction.Ignore;
        }

        if (_phase == Phase.Candidate)
        {
            if (cancelled)
            {
                Reset();
                return EdgeSwipeAction.Ignore;
            }

            Withhold(new EdgeSwipeSample(timeMs, _releaseX, _releaseY, Down: false));
            _phase = Phase.Idle;
            return EdgeSwipeAction.Decline;
        }

        Outcome = cancelled ? EdgeSwipeOutcome.Cancelled : Settle(timeMs);
        Zone = ZoneOf(_releaseX, _releaseY);
        _phase = Phase.Idle;
        return EdgeSwipeAction.Finish;
    }

    public int TakeWithheld(Span<EdgeSwipeSample> into)
    {
        var count = Math.Min(_withheldCount, into.Length);
        for (var i = 0; i < count; i++)
        {
            into[i] = _withheld[i];
        }

        _withheldCount = 0;
        return count;
    }

    public void Abort() => Reset();

    private void Reset()
    {
        _phase = Phase.Idle;
        _withheldCount = 0;
        _in = 0;
        _velocity = 0;
        _hasVelocity = false;
        _held = false;
        _reachedIn = false;
        _backedOut = false;
        _holdSince = 0;
    }

    private EdgeSwipeAction Decline()
    {
        _phase = Phase.Idle;
        return EdgeSwipeAction.Decline;
    }

    private void Withhold(in EdgeSwipeSample sample)
    {
        if (_withheldCount < WithheldCapacity)
        {
            _withheld[_withheldCount++] = sample;
        }
    }

    private void Track(double inward, uint timeMs)
    {
        var elapsed = timeMs - _lastMillis;
        if (elapsed > 0)
        {
            var instant = (inward - _in) * 1000.0 / elapsed;
            _velocity = _hasVelocity ? _velocity + (VelocitySmoothing * (instant - _velocity)) : instant;
            _hasVelocity = true;
            _lastMillis = timeMs;
        }

        _in = inward;

        var progress = Progress;
        if (progress >= CommitFraction)
        {
            _reachedIn = true;
            _backedOut = false;
        }
        else if (_reachedIn && progress <= ReturnFraction)
        {
            _backedOut = true;
        }

        if (Math.Abs(progress - HoldFraction) <= HoldTolerance)
        {
            if (_holdSince == 0)
            {
                _holdSince = timeMs;
            }
            else if (timeMs - _holdSince >= HoldMillis)
            {
                _held = true;
            }
        }
        else
        {
            _holdSince = 0;
        }
    }

    private EdgeSwipeOutcome Settle(uint timeMs)
    {
        if (_backedOut)
        {
            return EdgeSwipeOutcome.InAndBack;
        }

        if (_held)
        {
            return EdgeSwipeOutcome.Hold;
        }

        var progress = Progress;
        if (progress >= CommitFraction)
        {
            return EdgeSwipeOutcome.In;
        }

        var velocity = _hasVelocity && timeMs - _lastMillis < StaleVelocityMillis ? _velocity : 0;
        return velocity >= FlingPerSecond * Scale ? EdgeSwipeOutcome.In : EdgeSwipeOutcome.Cancelled;
    }

    private ScreenEdge EdgeAt(double x, double y, double width, double height)
    {
        var band = BandWidth * Scale;
        if (band <= 0)
        {
            return ScreenEdge.None;
        }

        var best = ScreenEdge.None;
        var nearest = double.MaxValue;
        Consider(ScreenEdge.Left, ScreenEdges.Left, x);
        Consider(ScreenEdge.Right, ScreenEdges.Right, width - x);
        Consider(ScreenEdge.Top, ScreenEdges.Top, y);
        Consider(ScreenEdge.Bottom, ScreenEdges.Bottom, height - y);
        return best;

        void Consider(ScreenEdge edge, ScreenEdges flag, double distance)
        {
            if ((Edges & flag) == 0 || distance < 0 || distance > band || distance >= nearest)
            {
                return;
            }

            nearest = distance;
            best = edge;
        }
    }

    private void Project(double x, double y, out double inward, out double along)
    {
        switch (_edge)
        {
            case ScreenEdge.Left:
                inward = x;
                along = y;
                break;
            case ScreenEdge.Right:
                inward = _width - x;
                along = y;
                break;
            case ScreenEdge.Top:
                inward = y;
                along = x;
                break;
            default:
                inward = _height - y;
                along = x;
                break;
        }
    }

    private EdgeSwipeZone ZoneOf(double x, double y)
    {
        if (_width <= 0 || _height <= 0)
        {
            return EdgeSwipeZone.None;
        }

        if (y >= _height * BottomZoneFraction)
        {
            return EdgeSwipeZone.Bottom;
        }

        if (x <= _width * SideZoneFraction)
        {
            return EdgeSwipeZone.Left;
        }

        return x >= _width * (1 - SideZoneFraction) ? EdgeSwipeZone.Right : EdgeSwipeZone.Middle;
    }

    private enum Phase
    {
        Idle,
        Candidate,
        Claimed,
    }
}
