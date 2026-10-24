using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Integrates weston's damped spring in fixed 4 ms steps, so the motion is the same at every refresh rate. The spring has no duration.
/// </summary>
public struct Spring
{
    private const double Step = 0.01;
    private const double StepMillis = 4.0;
    private const double SettleThreshold = 0.002;

    private long _timestampNanos;
    private bool _anchored;

    /// <summary>
    /// Initializes a new instance of the <see cref="Spring"/> struct at rest, with a friction of 400, limits 0 and 1, and <see cref="SpringClip.Overshoot"/>.
    /// </summary>
    /// <param name="k">The stiffness.</param>
    /// <param name="current">The start position.</param>
    /// <param name="target">The position that the spring moves toward.</param>
    public Spring(double k, double current, double target)
    {
        K = k;
        Friction = 400.0;
        Current = current;
        Previous = current;
        Target = target;
        Min = 0.0;
        Max = 1.0;
        Clip = SpringClip.Overshoot;
        _timestampNanos = 0;
        _anchored = false;
    }

    /// <summary>
    /// Gets or sets the stiffness.
    /// </summary>
    public double K { get; set; }

    /// <summary>
    /// Gets or sets the friction. weston's default of 400 rings at ordinary stiffness, so an opacity spring needs more.
    /// </summary>
    public double Friction { get; set; }

    /// <summary>
    /// Gets or sets the current position.
    /// </summary>
    public double Current { get; set; }

    /// <summary>
    /// Gets or sets the position one step ago. The difference to <see cref="Current"/> is the velocity.
    /// </summary>
    public double Previous { get; set; }

    /// <summary>
    /// Gets or sets the position that the spring moves toward.
    /// </summary>
    public double Target { get; set; }

    /// <summary>
    /// Gets or sets the lower limit for <see cref="Clip"/>.
    /// </summary>
    public double Min { get; set; }

    /// <summary>
    /// Gets or sets the upper limit for <see cref="Clip"/>.
    /// </summary>
    public double Max { get; set; }

    /// <summary>
    /// Gets or sets what the spring does past <see cref="Min"/> or <see cref="Max"/>.
    /// </summary>
    public SpringClip Clip { get; set; }

    /// <summary>
    /// Gets a value indicating whether the current and previous positions are both within 0.002 of the target.
    /// </summary>
    public readonly bool IsDone =>
        Math.Abs(Previous - Target) < SettleThreshold && Math.Abs(Current - Target) < SettleThreshold;

    /// <summary>
    /// Advances the spring to the predicted present time of a tick.
    /// </summary>
    /// <param name="now">The frame tick.</param>
    public void Update(in FrameTick now) => Update(now.TargetPresentNanos);

    /// <summary>
    /// Advances the spring in 4 ms steps up to a time. The first call after construction or <see cref="Retarget"/> only anchors the clock. A jump of more than one second advances one second.
    /// </summary>
    /// <param name="nowNanos">The time, in nanoseconds on the clock of the frame ticks.</param>
    public void Update(long nowNanos)
    {
        if (!_anchored)
        {
            _timestampNanos = nowNanos;
            _anchored = true;
            return;
        }

        var jumpMillis = (nowNanos - _timestampNanos) / 1_000_000.0;
        if (jumpMillis > 1000.0)
        {
            _timestampNanos = nowNanos - 1_000_000_000L;
        }

        while ((nowNanos - _timestampNanos) / 1_000_000.0 > StepMillis)
        {
            Advance();
            _timestampNanos += (long)(StepMillis * 1_000_000L);
        }
    }

    /// <summary>
    /// Sets a new target and keeps the position and the velocity. The next <see cref="Update(long)"/> anchors the clock again.
    /// </summary>
    /// <param name="target">The new target.</param>
    public void Retarget(double target)
    {
        Target = target;
        _anchored = false;
    }

    private void Advance()
    {
        var current = Current;
        var velocity = current - Previous;
        var force = (K * (Target - current) / 10.0) + (Previous - current) - (velocity * Friction);

        Current = current + (current - Previous) + (force * Step * Step);
        Previous = current;

        switch (Clip)
        {
            case SpringClip.Clamp:
                if (Current > Max)
                {
                    Current = Max;
                    Previous = Max;
                }
                else if (Current < 0.0)
                {
                    Current = Min;
                    Previous = Min;
                }

                break;

            case SpringClip.Bounce:
                if (Current > Max)
                {
                    Current = (2 * Max) - Current;
                    Previous = (2 * Max) - Previous;
                }
                else if (Current < Min)
                {
                    Current = (2 * Min) - Current;
                    Previous = (2 * Min) - Previous;
                }

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Calculates the position of a spring from 0 to 1 with a friction of 400 and no start velocity, after a time.
    /// </summary>
    /// <param name="k">The stiffness.</param>
    /// <param name="millis">The time, in milliseconds.</param>
    /// <returns>The position after the whole 4 ms steps in <paramref name="millis"/>.</returns>
    public static double Sample(double k, double millis) => Sample(k, 400.0, 0.0, millis);

    /// <summary>
    /// Calculates the position of a spring from 0 to 1 after a time.
    /// </summary>
    /// <param name="k">The stiffness.</param>
    /// <param name="friction">The friction. Zero or less uses 400.</param>
    /// <param name="initialVelocity">The start velocity, in position units per step.</param>
    /// <param name="millis">The time, in milliseconds.</param>
    /// <returns>The position after the whole 4 ms steps in <paramref name="millis"/>.</returns>
    public static double Sample(double k, double friction, double initialVelocity, double millis)
    {
        var spring = Normalized(k, friction, initialVelocity);
        var steps = (int)(millis / StepMillis);
        for (var i = 0; i < steps; i++)
        {
            spring.Advance();
        }

        return spring.Current;
    }

    /// <summary>
    /// Calculates the time that a spring from 0 to 1 with a friction of 400 and no start velocity takes to settle.
    /// </summary>
    /// <param name="k">The stiffness.</param>
    /// <returns>The time in milliseconds until <see cref="IsDone"/>, at most 10 seconds.</returns>
    public static double SettleMillis(double k) => SettleMillis(k, 400.0, 0.0);

    /// <summary>
    /// Calculates the time that a spring from 0 to 1 takes to settle.
    /// </summary>
    /// <param name="k">The stiffness.</param>
    /// <param name="friction">The friction. Zero or less uses 400.</param>
    /// <param name="initialVelocity">The start velocity, in position units per step.</param>
    /// <returns>The time in milliseconds until <see cref="IsDone"/>, at most 10 seconds.</returns>
    public static double SettleMillis(double k, double friction, double initialVelocity)
    {
        var spring = Normalized(k, friction, initialVelocity);
        var steps = 0;
        while (!spring.IsDone && steps < 2500)
        {
            spring.Advance();
            steps++;
        }

        return steps * StepMillis;
    }

    private static Spring Normalized(double k, double friction, double initialVelocity)
    {
        var spring = new Spring(k, 0.0, 1.0) { Friction = friction <= 0 ? 400.0 : friction };
        spring.Previous = -initialVelocity;
        return spring;
    }
}
