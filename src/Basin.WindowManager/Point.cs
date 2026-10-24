namespace Basin.WindowManager;

/// <summary>
/// A position in the compositor's logical coordinate space, or an offset relative to a window or surface.
/// </summary>
/// <param name="X">The horizontal coordinate in logical pixels.</param>
/// <param name="Y">The vertical coordinate in logical pixels.</param>
public readonly record struct Point(int X, int Y)
{
    /// <summary>
    /// Gets the origin, (0, 0).
    /// </summary>
    public static Point Zero => default;
}
