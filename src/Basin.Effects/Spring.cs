using Basin.Scene;

namespace Basin.Effects;

public struct Spring
{
    private const double Step = 0.01;
    private const double StepMillis = 4.0;
    private const double SettleThreshold = 0.002;

    private long _timestampNanos;
    private bool _anchored;

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

    public double K { get; set; }

    public double Friction { get; set; }

    public double Current { get; set; }

    public double Previous { get; set; }

    public double Target { get; set; }

    public double Min { get; set; }

    public double Max { get; set; }

    public SpringClip Clip { get; set; }

    public readonly bool IsDone =>
        Math.Abs(Previous - Target) < SettleThreshold && Math.Abs(Current - Target) < SettleThreshold;

    public void Update(in FrameTick now) => Update(now.TargetPresentNanos);

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

    public static double Sample(double k, double millis) => Sample(k, 400.0, 0.0, millis);

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

    public static double SettleMillis(double k) => SettleMillis(k, 400.0, 0.0);

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
