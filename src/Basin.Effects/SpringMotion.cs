namespace Basin.Effects;

public struct SpringMotion
{
    private const double Timestep = 1.0 / 100.0;

    private double _previousPosition;
    private double _previousVelocity;
    private double _nextPosition;
    private double _nextVelocity;
    private double _t;

    public SpringMotion(double springConstant = 200.0, double dampingRatio = 1.1)
    {
        SpringConstant = springConstant;
        DampingRatio = dampingRatio;
        Epsilon = 1.0;
        _t = 1.0;
    }

    public double SpringConstant { get; set; }

    public double DampingRatio { get; set; }

    public double Epsilon { get; set; }

    public double Anchor { get; set; }

    public readonly double Position => Lerp(_previousPosition, _nextPosition, _t);

    public readonly double Velocity => Lerp(_previousVelocity, _nextVelocity, _t);

    public readonly bool IsMoving => Math.Abs(Position - Anchor) > Epsilon || Math.Abs(Velocity) > Epsilon;

    public void SetPosition(double position)
    {
        var velocity = Velocity;
        _nextPosition = position;
        _nextVelocity = velocity;
        _t = 1.0;
    }

    public void SetVelocity(double velocity)
    {
        var position = Position;
        _nextPosition = position;
        _nextVelocity = velocity;
        _t = 1.0;
    }

    public void Advance(double millis)
    {
        if (!IsMoving)
        {
            return;
        }

        if (double.IsInfinity(SpringConstant))
        {
            _previousPosition = Anchor;
            _previousVelocity = 0;
            _nextPosition = Anchor;
            _nextVelocity = 0;
            _t = 1.0;
            return;
        }

        var steps = millis / 1000.0 / Timestep;
        for (_t += steps; _t > 1.0; _t -= 1.0)
        {
            _previousPosition = _nextPosition;
            _previousVelocity = _nextVelocity;
            Integrate();
        }
    }

    private void Integrate()
    {
        var damping = 2 * Math.Sqrt(SpringConstant) * DampingRatio;
        var position = _nextPosition;
        var velocity = _nextVelocity;
        var anchor = Anchor;
        var constant = SpringConstant;

        static (double Dp, double Dv) Evaluate(
            double position, double velocity, double anchor, double constant, double damping,
            double dt, double dp, double dv)
        {
            var nextPosition = position + (dp * dt);
            var nextVelocity = velocity + (dv * dt);
            var spring = (anchor - nextPosition) * constant;
            var drag = -nextVelocity * damping;
            return (velocity, spring + drag);
        }

        var k1 = Evaluate(position, velocity, anchor, constant, damping, 0.0, 0, 0);
        var k2 = Evaluate(position, velocity, anchor, constant, damping, 0.5 * Timestep, k1.Dp, k1.Dv);
        var k3 = Evaluate(position, velocity, anchor, constant, damping, 0.5 * Timestep, k2.Dp, k2.Dv);
        var k4 = Evaluate(position, velocity, anchor, constant, damping, Timestep, k3.Dp, k3.Dv);

        var dpdt = (k1.Dp + (2 * k2.Dp) + (2 * k3.Dp) + k4.Dp) / 6.0;
        var dvdt = (k1.Dv + (2 * k2.Dv) + (2 * k3.Dv) + k4.Dv) / 6.0;
        _nextPosition = position + (dpdt * Timestep);
        _nextVelocity = velocity + (dvdt * Timestep);
    }

    private static double Lerp(double a, double b, double t) => (a * (1 - t)) + (b * t);
}
