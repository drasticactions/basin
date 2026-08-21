namespace EightWm;

internal struct Manipulation
{
    private const double MinimumSettleMs = 200;
    private const double MaximumSettleMs = 1000;
    private const double VelocitySmoothing = 0.4;
    private const uint StaleVelocityMillis = 60;

    private bool _panning;
    private bool _settling;
    private double _originX;
    private double _originY;
    private double _lastX;
    private double _lastY;
    private long _lastMillis;
    private double _startOffset;
    private double _target;
    private long _settleStart;
    private double _settleMillis;

    public double Raw { get; private set; }

    public double Velocity { get; private set; }

    public PanAxis Axis { get; private set; }

    public bool IsPanning => _panning;

    public bool IsSettling => _settling;

    public double Minimum { get; set; }

    public double Maximum { get; set; }

    public double RailSlop { get; set; } = 12;

    public PanAxis Rail { get; set; }

    public double Friction { get; set; } = 4;

    public double RubberBandFalloff { get; set; } = 4;

    public bool RubberBand { get; set; } = true;

    public bool ChainToParent { get; set; }

    public SnapKind Snap { get; set; }

    public double SnapInterval { get; set; }

    public double ProximityRange { get; set; } = 120;

    public bool Locked { get; set; }

    public Manipulation()
    {
    }

    public readonly double Offset
    {
        get
        {
            if (!RubberBand)
            {
                return Raw;
            }

            if (Raw < Minimum)
            {
                return Minimum - Damp(Minimum - Raw);
            }

            return Raw > Maximum ? Maximum + Damp(Raw - Maximum) : Raw;
        }
    }

    public void Reset(double offset)
    {
        Raw = offset;
        Velocity = 0;
        Axis = PanAxis.Undecided;
        _panning = false;
        _settling = false;
    }

    public void Begin(double x, double y, long nowMillis)
    {
        _panning = true;
        _settling = false;
        _originX = x;
        _originY = y;
        _lastX = x;
        _lastY = y;
        _lastMillis = nowMillis;
        Velocity = 0;
        Axis = Locked ? Axis : PanAxis.Undecided;
    }

    public double Pan(double x, double y, long nowMillis)
    {
        if (!_panning)
        {
            return 0;
        }

        if (Axis == PanAxis.Undecided)
        {
            var travelX = Math.Abs(x - _originX);
            var travelY = Math.Abs(y - _originY);
            if (Math.Max(travelX, travelY) < RailSlop)
            {
                _lastX = x;
                _lastY = y;
                return 0;
            }

            Axis = travelX >= travelY ? PanAxis.Horizontal : PanAxis.Vertical;
        }

        var delta = Axis == PanAxis.Horizontal ? x - _lastX : y - _lastY;
        _lastX = x;
        _lastY = y;

        var elapsed = nowMillis - _lastMillis;
        if (elapsed > 0)
        {
            var instant = delta * 1000.0 / elapsed;
            Velocity += VelocitySmoothing * (instant - Velocity);
            _lastMillis = nowMillis;
        }

        if (Rail != PanAxis.Undecided && Axis != Rail)
        {
            return 0;
        }

        var wanted = Raw + delta;
        if (!ChainToParent || RubberBand)
        {
            Raw = wanted;
            return 0;
        }

        if (wanted < Minimum)
        {
            Raw = Minimum;
            return wanted - Minimum;
        }

        if (wanted > Maximum)
        {
            Raw = Maximum;
            return wanted - Maximum;
        }

        Raw = wanted;
        return 0;
    }

    public void Release(long nowMillis, ReadOnlySpan<double> snapPoints = default)
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;
        Axis = Locked ? Axis : PanAxis.Undecided;

        var velocity = nowMillis - _lastMillis < StaleVelocityMillis ? Velocity : 0;
        var projected = Friction > 0 ? Raw + (velocity / Friction) : Raw;
        _target = Clamp(Attract(projected, snapPoints));
        _startOffset = Raw;
        _settleStart = nowMillis;
        var distance = Math.Abs(_target - _startOffset);
        _settleMillis = Math.Clamp(distance * 2, MinimumSettleMs, MaximumSettleMs);
        _settling = Math.Abs(_target - _startOffset) > 0.5;
        if (!_settling)
        {
            Raw = _target;
            Velocity = 0;
        }
    }

    public bool Advance(long nowMillis)
    {
        if (!_settling)
        {
            return false;
        }

        var elapsed = nowMillis - _settleStart;
        if (elapsed >= _settleMillis)
        {
            Raw = _target;
            Velocity = 0;
            _settling = false;
            return true;
        }

        var progress = Curves.Evaluate(AnimationCurve.Deceleration, elapsed / _settleMillis);
        Raw = _startOffset + ((_target - _startOffset) * progress);
        return true;
    }

    public void Abort()
    {
        _panning = false;
        _settling = false;
        Velocity = 0;
    }

    public readonly double Clamp(double value) => Math.Clamp(value, Minimum, Maximum);

    private readonly double Attract(double value, ReadOnlySpan<double> snapPoints)
    {
        switch (Snap)
        {
            case SnapKind.Mandatory when SnapInterval > 0:
                return Math.Round(value / SnapInterval) * SnapInterval;

            case SnapKind.Mandatory when snapPoints.Length > 0:
                return Nearest(value, snapPoints);

            case SnapKind.Proximity when snapPoints.Length > 0:
            {
                var nearest = Nearest(value, snapPoints);
                return Math.Abs(nearest - value) <= ProximityRange ? nearest : value;
            }

            case SnapKind.Proximity when SnapInterval > 0:
            {
                var nearest = Math.Round(value / SnapInterval) * SnapInterval;
                return Math.Abs(nearest - value) <= ProximityRange ? nearest : value;
            }

            default:
                return value;
        }
    }

    private static double Nearest(double value, ReadOnlySpan<double> points)
    {
        var best = points[0];
        var distance = Math.Abs(points[0] - value);
        for (var i = 1; i < points.Length; i++)
        {
            var candidate = Math.Abs(points[i] - value);
            if (candidate < distance)
            {
                distance = candidate;
                best = points[i];
            }
        }

        return best;
    }

    private readonly double Damp(double excess) =>
        RubberBandFalloff <= 0 ? excess : excess / (1 + (excess / (RubberBandFalloff * 100)));
}
