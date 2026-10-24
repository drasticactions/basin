namespace Basin.Seat;

/// <summary>
/// Recognizes one finger that swipes inward from an edge of an area. It keeps the first events back until it can tell a swipe from an ordinary touch.
/// </summary>
/// <remarks>
/// Distances are in the area's units and the length settings are multiplied by <see cref="Scale"/>. The recognizer tracks one contact at a time and does not allocate.
/// </remarks>
public sealed class EdgeSwipeRecognizer
{
    /// <summary>
    /// The largest number of samples kept back for one candidate. A candidate that fills it is declined.
    /// </summary>
    public const int WithheldCapacity = 32;

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
    private VelocityTracker _tracker;
    private uint _beganMillis;
    private uint _holdSince;
    private bool _held;
    private bool _reachedIn;
    private bool _backedOut;

    /// <summary>
    /// Gets or sets the width of the band along each edge in which a touch down can start a swipe, before <see cref="Scale"/>.
    /// </summary>
    public double BandWidth { get; set; } = 20;

    /// <summary>
    /// Gets or sets the factor applied to every length setting, for example the output scale.
    /// </summary>
    public double Scale { get; set; } = 1;

    /// <summary>
    /// Gets or sets the edges on which a swipe can start. When a touch is in the band of two edges, the nearer edge wins.
    /// </summary>
    public ScreenEdges Edges { get; set; } = ScreenEdges.All;

    /// <summary>
    /// Gets or sets the inward travel at which a candidate becomes a claimed swipe, before <see cref="Scale"/>.
    /// </summary>
    public double ClaimDistance { get; set; } = 8;

    /// <summary>
    /// Gets or sets the inward distance from the edge at which <see cref="Progress"/> reaches 1, before <see cref="Scale"/>.
    /// </summary>
    public double RevealDistance { get; set; } = 150;

    /// <summary>
    /// Gets or sets the travel along the edge that a candidate can make before it is declined, before <see cref="Scale"/>. The drift must also exceed the inward travel.
    /// </summary>
    public double AlongSlop { get; set; } = 16;

    /// <summary>
    /// Gets or sets the time in milliseconds that a candidate can stay undecided. A later motion declines it.
    /// </summary>
    public uint WithholdMillis { get; set; } = 100;

    /// <summary>
    /// Gets or sets the progress at or past which a release gives <see cref="EdgeSwipeOutcome.In"/>.
    /// </summary>
    public double CommitFraction { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the progress at or below which a swipe that reached the commit fraction counts as gone back.
    /// </summary>
    public double ReturnFraction { get; set; } = 0.15;

    /// <summary>
    /// Gets or sets the progress at which the finger must stay to give <see cref="EdgeSwipeOutcome.Hold"/>.
    /// </summary>
    public double HoldFraction { get; set; } = 0.33;

    /// <summary>
    /// Gets or sets how far the progress can move from <see cref="HoldFraction"/> and still count as a hold.
    /// </summary>
    public double HoldTolerance { get; set; } = 0.12;

    /// <summary>
    /// Gets or sets the time in milliseconds that the finger must stay near <see cref="HoldFraction"/> to count as a hold.
    /// </summary>
    public uint HoldMillis { get; set; } = 400;

    /// <summary>
    /// Gets or sets the inward speed in units per second, before <see cref="Scale"/>, at which a short release still gives <see cref="EdgeSwipeOutcome.In"/>.
    /// </summary>
    public double FlingPerSecond { get; set; } = 1200;

    /// <summary>
    /// Gets or sets the fraction of the area width that each side zone covers.
    /// </summary>
    public double SideZoneFraction { get; set; } = 0.25;

    /// <summary>
    /// Gets or sets the fraction of the area height at or below which the bottom zone starts.
    /// </summary>
    public double BottomZoneFraction { get; set; } = 0.75;

    /// <summary>
    /// Gets the edge of the current or last swipe.
    /// </summary>
    public ScreenEdge Edge => _edge;

    /// <summary>
    /// Gets a value indicating whether a contact is withheld and not yet claimed or declined.
    /// </summary>
    public bool IsCandidate => _phase == Phase.Candidate;

    /// <summary>
    /// Gets a value indicating whether the recognizer owns a contact as a swipe.
    /// </summary>
    public bool IsClaimed => _phase == Phase.Claimed;

    /// <summary>
    /// Gets the id of the contact that the recognizer tracks or last tracked.
    /// </summary>
    public int ContactId => _id;

    /// <summary>
    /// Gets the inward distance from the edge divided by the reveal distance, clamped to the range 0 to 1.
    /// </summary>
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

    /// <summary>
    /// Gets the smoothed inward speed in area units per second. It is zero until two samples exist.
    /// </summary>
    public double Velocity => _tracker.Smoothed.X;

    /// <summary>
    /// Gets the last horizontal position of the contact in area-local coordinates.
    /// </summary>
    public double X => _releaseX;

    /// <summary>
    /// Gets the last vertical position of the contact in area-local coordinates.
    /// </summary>
    public double Y => _releaseY;

    /// <summary>
    /// Gets the result of the last claimed swipe. It is <see cref="EdgeSwipeOutcome.None"/> until a swipe finishes.
    /// </summary>
    public EdgeSwipeOutcome Outcome { get; private set; }

    /// <summary>
    /// Gets the zone of the area in which the last claimed swipe was released.
    /// </summary>
    public EdgeSwipeZone Zone { get; private set; }

    /// <summary>
    /// Offers a touch down. A down in the band of an enabled edge becomes a candidate and is withheld.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position in area-local coordinates.</param>
    /// <param name="y">The vertical position in area-local coordinates.</param>
    /// <param name="width">The area width.</param>
    /// <param name="height">The area height.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns><see cref="EdgeSwipeAction.Withhold"/> for a candidate. <see cref="EdgeSwipeAction.Ignore"/> when a contact is already tracked, the area is empty or the point is not near an edge.</returns>
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
        _tracker.Reset();
        _held = false;
        _reachedIn = false;
        _backedOut = false;
        _holdSince = 0;
        _beganMillis = timeMs;
        Project(x, y, out _originIn, out _originAlong);
        _in = _originIn;
        _tracker.Add(_in, 0, 0);
        _releaseX = x;
        _releaseY = y;
        _withheldCount = 0;
        Withhold(new EdgeSwipeSample(timeMs, x, y, Down: true));
        return EdgeSwipeAction.Withhold;
    }

    /// <summary>
    /// Offers a motion of the tracked contact. A candidate is claimed, declined or kept back. A claimed swipe updates its progress.
    /// </summary>
    /// <param name="id">The contact id. Other contacts give <see cref="EdgeSwipeAction.Ignore"/>.</param>
    /// <param name="x">The horizontal position in area-local coordinates.</param>
    /// <param name="y">The vertical position in area-local coordinates.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns>What the caller must do with the event.</returns>
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

    /// <summary>
    /// Offers the lift of the tracked contact. A candidate is declined and a claimed swipe settles its <see cref="Outcome"/> and <see cref="Zone"/>.
    /// </summary>
    /// <param name="id">The contact id. Other contacts give <see cref="EdgeSwipeAction.Ignore"/>.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="cancelled"><see langword="true"/> when the contact was cancelled. A cancelled candidate is dropped with no replay.</param>
    /// <returns><see cref="EdgeSwipeAction.Decline"/> for a candidate, <see cref="EdgeSwipeAction.Finish"/> for a claimed swipe.</returns>
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

    /// <summary>
    /// Moves the withheld samples into a buffer and forgets them. A declined lift adds a final sample with <c>Down</c> false.
    /// </summary>
    /// <param name="into">The buffer. Samples that do not fit are lost.</param>
    /// <returns>The number of samples written.</returns>
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

    /// <summary>
    /// Drops the tracked contact and its withheld samples without a result.
    /// </summary>
    public void Abort() => Reset();

    private void Reset()
    {
        _phase = Phase.Idle;
        _withheldCount = 0;
        _in = 0;
        _tracker.Reset();
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
        _tracker.Add(inward, 0, Nanos(timeMs));
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

        var velocity = _tracker.Velocity(Nanos(timeMs)).X;
        return velocity >= FlingPerSecond * Scale ? EdgeSwipeOutcome.In : EdgeSwipeOutcome.Cancelled;
    }

    private long Nanos(uint timeMs) => (long)(uint)(timeMs - _beganMillis) * 1_000_000;

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
