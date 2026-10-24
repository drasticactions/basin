namespace Basin.Effects;

/// <summary>
/// The corner radii of a blurred region, in logical pixels. A radius of zero or less keeps that corner square.
/// </summary>
/// <param name="TopLeft">The radius of the top-left corner.</param>
/// <param name="TopRight">The radius of the top-right corner.</param>
/// <param name="BottomLeft">The radius of the bottom-left corner.</param>
/// <param name="BottomRight">The radius of the bottom-right corner.</param>
public readonly record struct BlurCorners(
    double TopLeft,
    double TopRight,
    double BottomLeft,
    double BottomRight)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BlurCorners"/> struct with one radius for all four corners.
    /// </summary>
    /// <param name="radius">The radius of every corner.</param>
    public BlurCorners(double radius)
        : this(radius, radius, radius, radius)
    {
    }

    /// <summary>
    /// Gets a value indicating whether no corner has a positive radius.
    /// </summary>
    public bool IsSquare => TopLeft <= 0 && TopRight <= 0 && BottomLeft <= 0 && BottomRight <= 0;
}
