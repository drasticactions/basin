namespace Basin;

/// <summary>
/// A rounded rectangle that clips a texture draw. The rectangle can be larger than the drawn box, for example the outer edge of a window frame.
/// </summary>
/// <param name="Radius">The corner radius. 0 turns the clip off.</param>
/// <param name="OuterWidth">The width of the rounded rectangle, or 0 for the width of the drawn box.</param>
/// <param name="OuterHeight">The height of the rounded rectangle, or 0 for the height of the drawn box.</param>
/// <param name="OffsetX">The horizontal position of the drawn box inside the rounded rectangle.</param>
/// <param name="OffsetY">The vertical position of the drawn box inside the rounded rectangle.</param>
public readonly record struct CornerClip(
    double Radius,
    double OuterWidth = 0,
    double OuterHeight = 0,
    double OffsetX = 0,
    double OffsetY = 0)
{
    /// <summary>
    /// Gets a value indicating whether the clip is off.
    /// </summary>
    public bool IsEmpty => Radius <= 0;

    /// <summary>
    /// Multiplies every length of the clip by a factor.
    /// </summary>
    /// <param name="factor">The factor, for example the scale of an output.</param>
    /// <returns>The scaled clip.</returns>
    public CornerClip Scale(double factor) =>
        new(Radius * factor, OuterWidth * factor, OuterHeight * factor, OffsetX * factor, OffsetY * factor);
}
