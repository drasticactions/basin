using Basin.Scene;

namespace Basin.Effects;

public readonly record struct EasingCurve(EasingKind Kind, double X1, double Y1, double X2, double Y2)
{
    public static readonly EasingCurve Linear = new(EasingKind.Linear, 0, 0, 0, 0);

    public static readonly EasingCurve Sigmoid = new(EasingKind.Sigmoid, 0, 0, 0, 0);

    public static readonly EasingCurve Circle = new(EasingKind.Circle, 0, 0, 0, 0);

    public static readonly EasingCurve InCurve = new(EasingKind.InCurve, 0, 0, 0, 0);

    public static readonly EasingCurve OutCurve = new(EasingKind.OutCurve, 0, 0, 0, 0);

    public static readonly EasingCurve InCubic = new(EasingKind.InCubic, 0, 0, 0, 0);

    public static readonly EasingCurve OutCubic = new(EasingKind.OutCubic, 0, 0, 0, 0);

    public static readonly EasingCurve InOutCubic = new(EasingKind.InOutCubic, 0, 0, 0, 0);

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
            case EasingKind.InCurve:
                return SineCurve(t, MixFactor(t));
            case EasingKind.OutCurve:
                return SineCurve(t, MixFactor(1 - t));
            case EasingKind.InCubic:
                return t * t * t;
            case EasingKind.OutCubic:
                var inverse = 1 - t;
                return 1 - (inverse * inverse * inverse);
            case EasingKind.InOutCubic:
                if (t < 0.5)
                {
                    return 4 * t * t * t;
                }

                var rest = (-2 * t) + 2;
                return 1 - (rest * rest * rest / 2);
            case EasingKind.CubicBezier:
                return SampleBezier(t);
            case EasingKind.Spring:
                return Effects.Spring.Sample(X1, Y1, Y2, t * X2);
            default:
                return t;
        }
    }

    private static double SineCurve(double t, double mix) =>
        ((Math.Sin((t * Math.PI) - (Math.PI / 2)) / 2) + 0.5) * mix + (t * (1 - mix));

    private static double MixFactor(double t) => Math.Clamp(1 - (t * 2) + 0.3, 0.0, 1.0);

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
