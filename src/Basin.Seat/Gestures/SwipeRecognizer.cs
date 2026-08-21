namespace Basin.Seat;

public sealed class SwipeRecognizer
{
    private const double RubberBandFalloff = 4.0;
    private const double VelocitySmoothing = 0.4;
    private const uint StaleVelocityMillis = 60;

    private double _raw;
    private double _width = 1;
    private double _velocity;
    private bool _hasVelocity;
    private uint _lastUpdateMillis;

    public SwipeRecognizer(uint fingers = 3) => Fingers = fingers;

    public uint Fingers { get; }

    public bool IsActive { get; private set; }

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

    public int Direction => Math.Sign(_raw);

    public double Velocity => _velocity;

    public double CommitFraction { get; set; } = 0.5;

    public double FlingPerSecond { get; set; } = 1200;

    public bool ClampLow { get; set; }

    public bool ClampHigh { get; set; }

    public bool Begin(uint fingers, double width, uint timeMs)
    {
        Abort();
        if (fingers != Fingers || width <= 0)
        {
            return false;
        }

        IsActive = true;
        _width = width;
        _lastUpdateMillis = timeMs;
        return true;
    }

    public bool Update(double dx, double dy, uint timeMs)
    {
        _ = dy;
        if (!IsActive)
        {
            return false;
        }

        _raw += dx / _width;

        var elapsed = timeMs - _lastUpdateMillis;
        if (elapsed > 0)
        {
            var instant = dx * 1000.0 / elapsed;
            _velocity = _hasVelocity ? _velocity + (VelocitySmoothing * (instant - _velocity)) : instant;
            _hasVelocity = true;
            _lastUpdateMillis = timeMs;
        }

        return true;
    }

    public SwipeOutcome End(bool cancelled, uint timeMs)
    {
        if (!IsActive)
        {
            return SwipeOutcome.None;
        }

        var progress = Progress;
        var velocity = _hasVelocity && timeMs - _lastUpdateMillis < StaleVelocityMillis ? _velocity : 0;
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

    public void Abort()
    {
        IsActive = false;
        _raw = 0;
        _velocity = 0;
        _hasVelocity = false;
        _lastUpdateMillis = 0;
        ClampLow = false;
        ClampHigh = false;
    }

    private static double Damp(double excess) => excess / (1 + (excess * RubberBandFalloff));
}
