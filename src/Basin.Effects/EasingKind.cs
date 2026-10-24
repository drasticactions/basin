using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Names the shape of an <see cref="EasingCurve"/>.
/// </summary>
public enum EasingKind
{
    /// <summary>
    /// Progress without easing.
    /// </summary>
    Linear,

    /// <summary>
    /// A logistic curve, normalized to pass through 0 and 1.
    /// </summary>
    Sigmoid,

    /// <summary>
    /// Two quarter circles that ease in and out.
    /// </summary>
    Circle,

    /// <summary>
    /// A CSS cubic Bézier with two control points.
    /// </summary>
    CubicBezier,

    /// <summary>
    /// A sample of a <see cref="Spring"/> over its settle time.
    /// </summary>
    Spring,

    /// <summary>
    /// The Qt ease-in curve, a sine blended against linear.
    /// </summary>
    InCurve,

    /// <summary>
    /// The Qt ease-out curve, a sine blended against linear.
    /// </summary>
    OutCurve,

    /// <summary>
    /// An exact cubic ease-in.
    /// </summary>
    InCubic,

    /// <summary>
    /// An exact cubic ease-out.
    /// </summary>
    OutCubic,

    /// <summary>
    /// An exact cubic ease-in and ease-out.
    /// </summary>
    InOutCubic,

    /// <summary>
    /// An exact sine ease-in and ease-out.
    /// </summary>
    InOutSine,
}
