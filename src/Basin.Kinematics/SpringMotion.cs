namespace Basin.Kinematics;

/// <summary>
/// Moves one value toward an anchor as a mass-spring-damper. It integrates with RK4 at a fixed hundredth of a second and interpolates between the samples.
/// </summary>
public struct SpringMotion
{
    private const double Timestep = 1.0 / 100.0;

    private double _previousPosition;
    private double _previousVelocity;
    private double _nextPosition;
    private double _nextVelocity;
    private double _t;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpringMotion"/> struct at position 0 with an <see cref="Epsilon"/> of 1. The expression <c>new SpringMotion()</c> does not call this constructor.
    /// </summary>
    /// <param name="springConstant">The spring constant. Infinity makes the value jump to the anchor.</param>
    /// <param name="dampingRatio">The damping ratio. 1 is critical damping.</param>
    public SpringMotion(double springConstant = 200.0, double dampingRatio = 1.1)
    {
        SpringConstant = springConstant;
        DampingRatio = dampingRatio;
        Epsilon = 1.0;
        _t = 1.0;
    }

    /// <summary>
    /// Gets or sets the spring constant. Infinity makes the next <see cref="Advance"/> jump to <see cref="Anchor"/>.
    /// </summary>
    public double SpringConstant { get; set; }

    /// <summary>
    /// Gets or sets the damping ratio. 1 is critical damping.
    /// </summary>
    public double DampingRatio { get; set; }

    /// <summary>
    /// Gets or sets the distance from the anchor and the speed below which the motion counts as settled.
    /// </summary>
    public double Epsilon { get; set; }

    /// <summary>
    /// Gets or sets the position that the spring pulls toward.
    /// </summary>
    public double Anchor { get; set; }

    /// <summary>
    /// Gets the position, interpolated between the last two integration samples.
    /// </summary>
    public readonly double Position => Lerp(_previousPosition, _nextPosition, _t);

    /// <summary>
    /// Gets the velocity in units per second, interpolated between the last two integration samples.
    /// </summary>
    public readonly double Velocity => Lerp(_previousVelocity, _nextVelocity, _t);

    /// <summary>
    /// Gets a value indicating whether the distance from the anchor or the speed is more than <see cref="Epsilon"/>.
    /// </summary>
    public readonly bool IsMoving => Math.Abs(Position - Anchor) > Epsilon || Math.Abs(Velocity) > Epsilon;

    /// <summary>
    /// Sets the position at once and keeps the current velocity.
    /// </summary>
    /// <param name="position">The new position.</param>
    public void SetPosition(double position)
    {
        var velocity = Velocity;
        _nextPosition = position;
        _nextVelocity = velocity;
        _t = 1.0;
    }

    /// <summary>
    /// Sets the velocity at once and keeps the current position.
    /// </summary>
    /// <param name="velocity">The new velocity, in units per second.</param>
    public void SetVelocity(double velocity)
    {
        var position = Position;
        _nextPosition = position;
        _nextVelocity = velocity;
        _t = 1.0;
    }

    /// <summary>
    /// Advances the motion by an elapsed time. A motion that is settled does not change.
    /// </summary>
    /// <param name="millis">The elapsed time in milliseconds.</param>
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
