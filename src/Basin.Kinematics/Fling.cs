namespace Basin.Kinematics;

/// <summary>
/// Moves a point in a straight line from an initial velocity with exponential decay. It evaluates a closed form, so the rest position does not depend on the frame rate. It allocates nothing.
/// </summary>
public struct Fling
{
    /// <summary>
    /// The decay rate per second that applies when <see cref="Friction"/> is not positive.
    /// </summary>
    public const double DefaultFriction = 4.0;

    /// <summary>
    /// The stop speed, in units per second, that applies when <see cref="StopSpeed"/> is not positive.
    /// </summary>
    public const double DefaultStopSpeed = 20.0;

    private TickDelta _clock;
    private double _x0;
    private double _y0;
    private double _vx0;
    private double _vy0;
    private long _elapsedNanos;
    private long _durationNanos;
    private bool _moving;

    /// <summary>
    /// Initializes a new instance of the <see cref="Fling"/> struct.
    /// </summary>
    /// <param name="friction">The decay rate per second.</param>
    /// <param name="stopSpeed">The speed, in units per second, at which the motion stops.</param>
    public Fling(double friction, double stopSpeed)
    {
        Friction = friction;
        StopSpeed = stopSpeed;
    }

    /// <summary>
    /// Gets or sets the decay rate of the speed, per second. A value that is not positive uses <see cref="DefaultFriction"/>. A change applies to the next <see cref="Start"/>.
    /// </summary>
    public double Friction { get; set; }

    /// <summary>
    /// Gets or sets the speed, in units per second, at which the motion stops. A value that is not positive uses <see cref="DefaultStopSpeed"/>.
    /// </summary>
    public double StopSpeed { get; set; }

    /// <summary>
    /// Gets a value indicating whether the motion has not yet reached its rest position.
    /// </summary>
    public readonly bool IsMoving => _moving;

    /// <summary>
    /// Gets the horizontal position at the elapsed time.
    /// </summary>
    public double X { readonly get; private set; }

    /// <summary>
    /// Gets the vertical position at the elapsed time.
    /// </summary>
    public double Y { readonly get; private set; }

    /// <summary>
    /// Gets the horizontal velocity at the elapsed time, in units per second. It is zero when the motion is stopped.
    /// </summary>
    public double VelocityX { readonly get; private set; }

    /// <summary>
    /// Gets the vertical velocity at the elapsed time, in units per second. It is zero when the motion is stopped.
    /// </summary>
    public double VelocityY { readonly get; private set; }

    /// <summary>
    /// Gets the position where the motion will stop, without running it. It is the current position when the motion is stopped.
    /// </summary>
    public readonly (double X, double Y) ProjectedRest
    {
        get
        {
            if (!_moving)
            {
                return (X, Y);
            }

            var (x, y) = RestFrom(_x0, _y0, _vx0, _vy0, EffectiveFriction, EffectiveStopSpeed);
            return (x, y);
        }
    }

    private readonly double EffectiveFriction => Friction > 0 ? Friction : DefaultFriction;

    private readonly double EffectiveStopSpeed => StopSpeed > 0 ? StopSpeed : DefaultStopSpeed;

    /// <summary>
    /// Starts a motion and resets the clock. A speed at or below the stop speed, or a speed that is not finite, does not start a motion.
    /// </summary>
    /// <param name="x">The horizontal start position.</param>
    /// <param name="y">The vertical start position.</param>
    /// <param name="velocityX">The horizontal start velocity, in units per second.</param>
    /// <param name="velocityY">The vertical start velocity, in units per second.</param>
    public void Start(double x, double y, double velocityX, double velocityY)
    {
        _x0 = X = x;
        _y0 = Y = y;
        _vx0 = velocityX;
        _vy0 = velocityY;
        _elapsedNanos = 0;
        _clock.Reset();

        var speed = Math.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
        var stop = EffectiveStopSpeed;
        _moving = speed > stop && double.IsFinite(speed);
        if (!_moving)
        {
            VelocityX = VelocityY = 0;
            _durationNanos = 0;
            return;
        }

        VelocityX = velocityX;
        VelocityY = velocityY;
        _durationNanos = (long)Math.Ceiling(Math.Log(speed / stop) / EffectiveFriction * 1e9);
    }

    /// <summary>
    /// Advances the motion to a frame tick. A tick that does not advance moves nothing, and the first tick after <see cref="Start"/> counts one refresh interval.
    /// </summary>
    /// <param name="tick">The frame tick of the output that repaints.</param>
    /// <returns><see langword="true"/> while the motion continues.</returns>
    public bool Step(in FrameTick tick)
    {
        if (!_moving)
        {
            return false;
        }

        Advance(_clock.Advance(tick));
        return _moving;
    }

    /// <summary>
    /// Advances the motion by an elapsed time. When the motion reaches its stop time, the position is <see cref="ProjectedRest"/> exactly.
    /// </summary>
    /// <param name="nanos">The elapsed time in nanoseconds. A value that is not positive does nothing.</param>
    public void Advance(long nanos)
    {
        if (!_moving || nanos <= 0)
        {
            return;
        }

        _elapsedNanos += nanos;
        if (_elapsedNanos >= _durationNanos)
        {
            (X, Y) = RestFrom(_x0, _y0, _vx0, _vy0, EffectiveFriction, EffectiveStopSpeed);
            VelocityX = VelocityY = 0;
            _moving = false;
            return;
        }

        var k = EffectiveFriction;
        var decay = Math.Exp(-k * (_elapsedNanos / 1e9));
        var travel = (1 - decay) / k;
        X = _x0 + (_vx0 * travel);
        Y = _y0 + (_vy0 * travel);
        VelocityX = _vx0 * decay;
        VelocityY = _vy0 * decay;
    }

    /// <summary>
    /// Stops the motion at its current position and sets the velocity to zero.
    /// </summary>
    public void Stop()
    {
        _moving = false;
        VelocityX = VelocityY = 0;
    }

    /// <summary>
    /// Calculates how far a fling travels before it stops.
    /// </summary>
    /// <param name="speed">The start speed, in units per second.</param>
    /// <param name="friction">The decay rate per second.</param>
    /// <param name="stopSpeed">The speed at which the motion stops.</param>
    /// <returns>The travel distance, or zero when the speed is at or below the stop speed or the friction is not positive.</returns>
    public static double RestDistance(double speed, double friction, double stopSpeed) =>
        speed <= stopSpeed || friction <= 0 ? 0 : (speed - stopSpeed) / friction;

    /// <summary>
    /// Calculates the start velocity that brings a fling to rest at a target. It is the inverse of <see cref="ProjectedRest"/>.
    /// </summary>
    /// <param name="fromX">The horizontal start position.</param>
    /// <param name="fromY">The vertical start position.</param>
    /// <param name="toX">The horizontal rest position.</param>
    /// <param name="toY">The vertical rest position.</param>
    /// <param name="friction">The decay rate per second.</param>
    /// <param name="stopSpeed">The speed at which the motion stops. A negative value counts as zero.</param>
    /// <returns>The start velocity, or zero when the points are equal or the friction is not positive.</returns>
    public static (double X, double Y) ThrowTo(
        double fromX, double fromY, double toX, double toY, double friction, double stopSpeed)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance <= 0 || friction <= 0)
        {
            return (0, 0);
        }

        var speed = (distance * friction) + Math.Max(stopSpeed, 0);
        return (dx / distance * speed, dy / distance * speed);
    }

    private static (double X, double Y) RestFrom(
        double x, double y, double vx, double vy, double friction, double stopSpeed)
    {
        var speed = Math.Sqrt((vx * vx) + (vy * vy));
        if (speed <= stopSpeed)
        {
            return (x, y);
        }

        var scale = (speed - stopSpeed) / friction / speed;
        return (x + (vx * scale), y + (vy * scale));
    }
}
