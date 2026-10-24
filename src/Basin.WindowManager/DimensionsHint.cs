using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// The minimum and maximum dimensions that a window prefers, in logical pixels.
/// A zero component means that the window has no preference for that bound.
/// </summary>
/// <param name="Minimum">The smallest size the window prefers. A zero component has no lower bound.</param>
/// <param name="Maximum">The largest size the window prefers. A zero component has no upper bound.</param>
public readonly record struct DimensionsHint(Size Minimum, Size Maximum)
{
    /// <summary>
    /// Gets a value indicating whether the window cannot be resized.
    /// This is true only when both minimum components are positive and equal to the maximum components.
    /// </summary>
    public bool IsFixed =>
        Minimum.Width > 0 && Minimum.Width == Maximum.Width
        && Minimum.Height > 0 && Minimum.Height == Maximum.Height;

    /// <summary>
    /// Limits a size to the hinted range. A zero bound does not limit its component.
    /// </summary>
    /// <param name="size">The size to limit, in logical pixels.</param>
    /// <returns>The size with each component no larger than a positive maximum and no smaller than the minimum.</returns>
    public Size Clamp(Size size)
    {
        var width = size.Width;
        var height = size.Height;
        if (Maximum.Width > 0)
        {
            width = Math.Min(width, Maximum.Width);
        }

        if (Maximum.Height > 0)
        {
            height = Math.Min(height, Maximum.Height);
        }

        return new Size(Math.Max(width, Minimum.Width), Math.Max(height, Minimum.Height));
    }
}
