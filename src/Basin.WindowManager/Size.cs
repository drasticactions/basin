namespace Basin.WindowManager;

/// <summary>
/// A size in the compositor's logical coordinate space. Zero means "not yet known" or, where a request accepts it, "the recipient chooses".
/// </summary>
/// <param name="Width">The width in logical pixels.</param>
/// <param name="Height">The height in logical pixels.</param>
public readonly record struct Size(int Width, int Height)
{
    /// <summary>
    /// Gets the size with both components zero.
    /// </summary>
    public static Size Zero => default;

    /// <summary>
    /// Gets a value indicating whether the size covers no area, because a component is zero or negative.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
