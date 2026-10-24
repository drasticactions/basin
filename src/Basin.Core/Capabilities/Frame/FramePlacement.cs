namespace Basin.Capabilities;

/// <summary>
/// Where the client area sits on its output, for a look that draws something fixed to the screen. Values are in logical pixels.
/// </summary>
/// <param name="X">The horizontal position of the client origin on the output.</param>
/// <param name="Y">The vertical position of the client origin on the output.</param>
/// <param name="OutputWidth">The logical width of the output.</param>
/// <param name="OutputHeight">The logical height of the output.</param>
public readonly record struct FramePlacement(int X, int Y, int OutputWidth, int OutputHeight)
{
    /// <summary>
    /// Gets a value indicating whether the placement is not known, which an output with no area means.
    /// </summary>
    public bool IsEmpty => OutputWidth <= 0 || OutputHeight <= 0;
}
