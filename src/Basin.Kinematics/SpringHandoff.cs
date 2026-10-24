namespace Basin.Kinematics;

/// <summary>
/// Moves a point toward a target on two springs, one for each axis, from a start position and a start velocity. A released gesture then continues at the speed of the finger. It allocates nothing.
/// </summary>
public struct SpringHandoff
{
    /// <summary>
    /// The stiffness that applies when <see cref="Stiffness"/> is not positive.
    /// </summary>
    public const double DefaultStiffness = 200.0;

    /// <summary>
    /// The damping ratio that applies when <see cref="DampingRatio"/> is not positive.
    /// </summary>
    public const double DefaultDampingRatio = 1.0;

    /// <summary>
    /// The settle distance that applies when <see cref="Epsilon"/> is not positive.
    /// </summary>
    public const double DefaultEpsilon = 0.5;

    private SpringMotion _x;
    private SpringMotion _y;
    private TickDelta _clock;
    private bool _moving;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpringHandoff"/> struct.
    /// </summary>
    /// <param name="stiffness">The spring constant.</param>
    /// <param name="dampingRatio">The damping ratio. 1 is critical damping.</param>
    public SpringHandoff(double stiffness, double dampingRatio)
    {
        Stiffness = stiffness;
        DampingRatio = dampingRatio;
    }

    /// <summary>
    /// Gets or sets the spring constant. A value that is not positive uses <see cref="DefaultStiffness"/>. A change applies to the next <see cref="Start"/>.
    /// </summary>
    public double Stiffness { get; set; }

    /// <summary>
    /// Gets or sets the damping ratio. A value that is not positive uses <see cref="DefaultDampingRatio"/>. A change applies to the next <see cref="Start"/>.
    /// </summary>
    public double DampingRatio { get; set; }

    /// <summary>
    /// Gets or sets the distance and the speed below which each axis counts as settled. A value that is not positive uses <see cref="DefaultEpsilon"/>.
    /// </summary>
    public double Epsilon { get; set; }

    /// <summary>
    /// Gets a value indicating whether either axis has not settled.
    /// </summary>
    public readonly bool IsMoving => _moving;

    /// <summary>
    /// Gets the horizontal target.
    /// </summary>
    public double TargetX { readonly get; private set; }

    /// <summary>
    /// Gets the vertical target.
    /// </summary>
    public double TargetY { readonly get; private set; }

    /// <summary>
    /// Gets the horizontal position. It is <see cref="TargetX"/> exactly when the spring is not moving.
    /// </summary>
    public readonly double X => _moving ? _x.Position : TargetX;

    /// <summary>
    /// Gets the vertical position. It is <see cref="TargetY"/> exactly when the spring is not moving.
    /// </summary>
    public readonly double Y => _moving ? _y.Position : TargetY;

    /// <summary>
    /// Gets the horizontal velocity, in units per second. It is zero when the spring is not moving.
    /// </summary>
    public readonly double VelocityX => _moving ? _x.Velocity : 0;

    /// <summary>
    /// Gets the vertical velocity, in units per second. It is zero when the spring is not moving.
    /// </summary>
    public readonly double VelocityY => _moving ? _y.Velocity : 0;

    /// <summary>
    /// Starts the springs and resets the clock. A start that is already settled within <see cref="Epsilon"/> does not move.
    /// </summary>
    /// <param name="x">The horizontal start position.</param>
    /// <param name="y">The vertical start position.</param>
    /// <param name="velocityX">The horizontal start velocity, in units per second.</param>
    /// <param name="velocityY">The vertical start velocity, in units per second.</param>
    /// <param name="targetX">The horizontal target.</param>
    /// <param name="targetY">The vertical target.</param>
    public void Start(double x, double y, double velocityX, double velocityY, double targetX, double targetY)
    {
        var stiffness = Stiffness > 0 ? Stiffness : DefaultStiffness;
        var damping = DampingRatio > 0 ? DampingRatio : DefaultDampingRatio;
        var epsilon = Epsilon > 0 ? Epsilon : DefaultEpsilon;
        _x = new SpringMotion(stiffness, damping) { Epsilon = epsilon, Anchor = targetX };
        _y = new SpringMotion(stiffness, damping) { Epsilon = epsilon, Anchor = targetY };
        _x.SetPosition(x);
        _x.SetVelocity(velocityX);
        _y.SetPosition(y);
        _y.SetVelocity(velocityY);
        TargetX = targetX;
        TargetY = targetY;
        _clock.Reset();
        _moving = _x.IsMoving || _y.IsMoving;
    }

    /// <summary>
    /// Changes the target and keeps the current position and velocity. A spring that is not moving does not start again, so its position jumps to the new target.
    /// </summary>
    /// <param name="targetX">The new horizontal target.</param>
    /// <param name="targetY">The new vertical target.</param>
    public void Retarget(double targetX, double targetY)
    {
        TargetX = targetX;
        TargetY = targetY;
        if (!_moving)
        {
            return;
        }

        _x.Anchor = targetX;
        _y.Anchor = targetY;
    }

    /// <summary>
    /// Advances the springs to a frame tick. A tick that does not advance moves nothing, and the first tick after <see cref="Start"/> counts one refresh interval.
    /// </summary>
    /// <param name="tick">The frame tick of the output that repaints.</param>
    /// <returns><see langword="true"/> while the spring moves.</returns>
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
    /// Advances the springs by an elapsed time.
    /// </summary>
    /// <param name="nanos">The elapsed time in nanoseconds. A value that is not positive does nothing.</param>
    public void Advance(long nanos)
    {
        if (!_moving || nanos <= 0)
        {
            return;
        }

        var millis = nanos / 1e6;
        _x.Advance(millis);
        _y.Advance(millis);
        _moving = _x.IsMoving || _y.IsMoving;
    }

    /// <summary>
    /// Stops the motion. The position becomes the target at once.
    /// </summary>
    public void Stop() => _moving = false;
}
