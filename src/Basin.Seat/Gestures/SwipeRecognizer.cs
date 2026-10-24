namespace Basin.Seat;

/// <summary>
/// Recognizes a touchpad swipe of a fixed finger count from its deltas, with progress, rubber band limits and a fling.
/// </summary>
public sealed class SwipeRecognizer
{
    private const double RubberBandFalloff = 4.0;

    private double _raw;
    private double _width = 1;
    private double _travel;
    private VelocityTracker _tracker;
    private uint _beganMillis;

    /// <summary>
    /// Initializes a new instance of the <see cref="SwipeRecognizer"/> class.
    /// </summary>
    /// <param name="fingers">The finger count that <see cref="Begin"/> accepts.</param>
    public SwipeRecognizer(uint fingers = 3) => Fingers = fingers;

    /// <summary>
    /// Gets the finger count that a swipe must have.
    /// </summary>
    public uint Fingers { get; }

    /// <summary>
    /// Gets or sets the axis along which the deltas count.
    /// </summary>
    public SwipeAxis Axis { get; set; } = SwipeAxis.Horizontal;

    /// <summary>
    /// Gets a value indicating whether a swipe began and has not ended or aborted.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the travel divided by the width, in the range -1 to 1. Past a clamped limit, the value grows slower and stays under 0.25.
    /// </summary>
    public double Progress
    {
        get
        {
            if (_raw > 0)
            {
                return ClampHigh ? Damp(_raw) : Math.Min(_raw, 1);
            }

            return _raw < 0
                ? ClampLow ? -Damp(-_raw) : Math.Max(_raw, -1)
                : 0;
        }
    }

    /// <summary>
    /// Gets the sign of the travel: 1, -1, or 0 with no travel.
    /// </summary>
    public int Direction => Math.Sign(_raw);

    /// <summary>
    /// Gets the smoothed speed along the axis in input units per second.
    /// </summary>
    public double Velocity => _tracker.Smoothed.X;

    /// <summary>
    /// Gets the speed at the last <see cref="End"/> as widths per second. It is zero after a cancel.
    /// </summary>
    public double ReleaseVelocity { get; private set; }

    /// <summary>
    /// Gets or sets the absolute progress at or past which a release commits.
    /// </summary>
    public double CommitFraction { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the speed in input units per second at which a short release still commits.
    /// </summary>
    public double FlingPerSecond { get; set; } = 1200;

    /// <summary>
    /// Gets or sets a value indicating whether negative travel is past a limit. It damps the progress and makes a release there cancel. <see cref="Abort"/> clears it.
    /// </summary>
    public bool ClampLow { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether positive travel is past a limit. It damps the progress and makes a release there cancel. <see cref="Abort"/> clears it.
    /// </summary>
    public bool ClampHigh { get; set; }

    /// <summary>
    /// Starts a swipe. It aborts a swipe that is still active.
    /// </summary>
    /// <param name="fingers">The finger count of the touchpad gesture.</param>
    /// <param name="width">The travel that gives a progress of 1, in input units.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns><see langword="false"/> when the finger count is not <see cref="Fingers"/> or the width is not positive.</returns>
    public bool Begin(uint fingers, double width, uint timeMs)
    {
        Abort();
        ReleaseVelocity = 0;
        if (fingers != Fingers || width <= 0)
        {
            return false;
        }

        IsActive = true;
        _width = width;
        _beganMillis = timeMs;
        _tracker.Add(0, 0, 0);
        return true;
    }

    /// <summary>
    /// Adds a gesture delta. Only the component along <see cref="Axis"/> counts.
    /// </summary>
    /// <param name="dx">The horizontal delta in input units.</param>
    /// <param name="dy">The vertical delta in input units.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns><see langword="false"/> when no swipe is active.</returns>
    public bool Update(double dx, double dy, uint timeMs)
    {
        if (!IsActive)
        {
            return false;
        }

        var along = Axis == SwipeAxis.Vertical ? dy : dx;
        _raw += along / _width;
        _travel += along;
        _tracker.Add(_travel, 0, Nanos(timeMs));
        return true;
    }

    /// <summary>
    /// Ends the swipe and decides whether it commits. The recognizer is then inactive and its clamps are cleared.
    /// </summary>
    /// <param name="cancelled"><see langword="true"/> when the input device cancelled the gesture.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns>The outcome, or <see cref="SwipeOutcome.None"/> when no swipe was active.</returns>
    public SwipeOutcome End(bool cancelled, uint timeMs)
    {
        if (!IsActive)
        {
            return SwipeOutcome.None;
        }

        var progress = Progress;
        var velocity = _tracker.Velocity(Nanos(timeMs)).X;
        ReleaseVelocity = cancelled ? 0 : velocity / _width;
        var againstLimit = (_raw > 0 && ClampHigh) || (_raw < 0 && ClampLow);
        Abort();

        if (cancelled || againstLimit || progress == 0)
        {
            return SwipeOutcome.Cancel;
        }

        if (Math.Abs(progress) >= CommitFraction)
        {
            return SwipeOutcome.Commit;
        }

        return Math.Abs(velocity) >= FlingPerSecond && Math.Sign(velocity) == Math.Sign(progress)
            ? SwipeOutcome.Commit
            : SwipeOutcome.Cancel;
    }

    /// <summary>
    /// Drops the swipe with no outcome and clears both clamps.
    /// </summary>
    public void Abort()
    {
        IsActive = false;
        _raw = 0;
        _travel = 0;
        _tracker.Reset();
        _beganMillis = 0;
        ClampLow = false;
        ClampHigh = false;
    }

    private long Nanos(uint timeMs) => (long)(uint)(timeMs - _beganMillis) * 1_000_000;

    private static double Damp(double excess) => excess / (1 + (excess * RubberBandFalloff));
}
