using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// An easing shape as a value. The four parameters mean different things for each kind.
/// </summary>
/// <param name="Kind">The shape.</param>
/// <param name="X1">The first control X of a Bézier, or the stiffness of a spring.</param>
/// <param name="Y1">The first control Y of a Bézier, or the friction of a spring.</param>
/// <param name="X2">The second control X of a Bézier, or the settle time of a spring in milliseconds.</param>
/// <param name="Y2">The second control Y of a Bézier, or the initial velocity of a spring.</param>
public readonly record struct EasingCurve(EasingKind Kind, double X1, double Y1, double X2, double Y2)
{
    /// <summary>
    /// Progress without easing.
    /// </summary>
    public static readonly EasingCurve Linear = new(EasingKind.Linear, 0, 0, 0, 0);

    /// <summary>
    /// A logistic curve, normalized to pass through 0 and 1.
    /// </summary>
    public static readonly EasingCurve Sigmoid = new(EasingKind.Sigmoid, 0, 0, 0, 0);

    /// <summary>
    /// Two quarter circles that ease in and out.
    /// </summary>
    public static readonly EasingCurve Circle = new(EasingKind.Circle, 0, 0, 0, 0);

    /// <summary>
    /// The Qt ease-in curve, a sine blended against linear over the first part.
    /// </summary>
    public static readonly EasingCurve InCurve = new(EasingKind.InCurve, 0, 0, 0, 0);

    /// <summary>
    /// The Qt ease-out curve, a sine blended against linear over the last part.
    /// </summary>
    public static readonly EasingCurve OutCurve = new(EasingKind.OutCurve, 0, 0, 0, 0);

    /// <summary>
    /// An exact cubic ease-in.
    /// </summary>
    public static readonly EasingCurve InCubic = new(EasingKind.InCubic, 0, 0, 0, 0);

    /// <summary>
    /// An exact cubic ease-out.
    /// </summary>
    public static readonly EasingCurve OutCubic = new(EasingKind.OutCubic, 0, 0, 0, 0);

    /// <summary>
    /// An exact cubic ease-in and ease-out.
    /// </summary>
    public static readonly EasingCurve InOutCubic = new(EasingKind.InOutCubic, 0, 0, 0, 0);

    /// <summary>
    /// An exact sine ease-in and ease-out.
    /// </summary>
    public static readonly EasingCurve InOutSine = new(EasingKind.InOutSine, 0, 0, 0, 0);

    /// <summary>
    /// Creates a CSS cubic Bézier from 0,0 to 1,1.
    /// </summary>
    /// <param name="x1">The X of the first control point, from 0 to 1.</param>
    /// <param name="y1">The Y of the first control point.</param>
    /// <param name="x2">The X of the second control point, from 0 to 1.</param>
    /// <param name="y2">The Y of the second control point.</param>
    /// <returns>The curve.</returns>
    public static EasingCurve CubicBezier(double x1, double y1, double x2, double y2) =>
        new(EasingKind.CubicBezier, x1, y1, x2, y2);

    /// <summary>
    /// Creates a spring curve with the default friction of 400. That friction is underdamped and rings at ordinary stiffness.
    /// </summary>
    /// <param name="k">The spring stiffness.</param>
    /// <returns>The curve, which an opacity easing must not use.</returns>
    public static EasingCurve Spring(double k) => Spring(k, 400.0, 0.0);

    /// <summary>
    /// Creates a curve that samples a <see cref="Basin.Effects.Spring"/> over its settle time. The settle time is measured once, here.
    /// </summary>
    /// <param name="k">The spring stiffness.</param>
    /// <param name="friction">The friction. An opacity easing needs a damped value, for example 1400 or 4000.</param>
    /// <param name="initialVelocity">The start velocity toward the target.</param>
    /// <returns>The curve.</returns>
    public static EasingCurve Spring(double k, double friction, double initialVelocity = 0.0) =>
        new(
            EasingKind.Spring,
            k,
            friction,
            Effects.Spring.SettleMillis(k, friction, initialVelocity),
            initialVelocity);

    /// <summary>
    /// Gets the time the spring takes to settle, in milliseconds. It is 0 for every other kind.
    /// </summary>
    public double SettleMillis => Kind == EasingKind.Spring ? X2 : 0.0;

    /// <summary>
    /// Maps a raw fraction through the curve.
    /// </summary>
    /// <param name="t">The raw fraction. The method clamps it to the range from 0 to 1.</param>
    /// <returns>The eased value. A spring or a Bézier can go past the range from 0 to 1.</returns>
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
            case EasingKind.InOutSine:
                return (1 - Math.Cos(t * Math.PI)) / 2;
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
