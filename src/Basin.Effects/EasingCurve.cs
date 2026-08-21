using Basin.Scene;

namespace Basin.Effects;

public readonly record struct EasingCurve(EasingKind Kind, double X1, double Y1, double X2, double Y2)
{
    public static readonly EasingCurve Linear = new(EasingKind.Linear, 0, 0, 0, 0);

    public static readonly EasingCurve Sigmoid = new(EasingKind.Sigmoid, 0, 0, 0, 0);

    public static readonly EasingCurve Circle = new(EasingKind.Circle, 0, 0, 0, 0);

    public static EasingCurve CubicBezier(double x1, double y1, double x2, double y2) =>
        new(EasingKind.CubicBezier, x1, y1, x2, y2);

    public static EasingCurve Spring(double k) => Spring(k, 400.0, 0.0);

    public static EasingCurve Spring(double k, double friction, double initialVelocity = 0.0) =>
        new(
            EasingKind.Spring,
            k,
            friction,
            Effects.Spring.SettleMillis(k, friction, initialVelocity),
            initialVelocity);

    public double SettleMillis => Kind == EasingKind.Spring ? X2 : 0.0;

    public double Apply(double t)
    {
        t = Math.Clamp(t, 0, 1);
        switch (Kind)
        {
            case EasingKind.Sigmoid:
                var raw = 1.0 / (1.0 + Math.Exp(-12.0 * t + 6.0));
                var low = 1.0 / (1.0 + Math.Exp(6.0));
                var high = 1.0 / (1.0 + Math.Exp(-6.0));
                return (raw - low) / (high - low);
            case EasingKind.Circle:
                return t <= 0.5
                    ? (1.0 - Math.Sqrt(1.0 - (2.0 * t * 2.0 * t))) / 2.0
                    : (1.0 + Math.Sqrt(1.0 - (((2.0 * t) - 2.0) * ((2.0 * t) - 2.0)))) / 2.0;
            case EasingKind.CubicBezier:
                return SampleBezier(t);
            case EasingKind.Spring:
                return Effects.Spring.Sample(X1, Y1, Y2, t * X2);
            default:
                return t;
        }
    }

    private double SampleBezier(double x)
    {
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var current = BezierAxis(t, X1, X2) - x;
            if (Math.Abs(current) < 1e-6)
            {
                break;
            }

            var derivative = BezierAxisDerivative(t, X1, X2);
            if (Math.Abs(derivative) < 1e-9)
            {
                break;
            }

            t = Math.Clamp(t - (current / derivative), 0, 1);
        }

        return BezierAxis(t, Y1, Y2);
    }

    private static double BezierAxis(double t, double p1, double p2)
    {
        var inverse = 1 - t;
        return (3 * inverse * inverse * t * p1) + (3 * inverse * t * t * p2) + (t * t * t);
    }

    private static double BezierAxisDerivative(double t, double p1, double p2)
    {
        var inverse = 1 - t;
        return (3 * inverse * inverse * p1) + (6 * inverse * t * (p2 - p1)) + (3 * t * t * (1 - p2));
    }
}
