namespace Basin.Scene;

/// <summary>
/// The sizes of a <see cref="UIStripFrame"/> in logical pixels.
/// </summary>
/// <param name="Margin">The shadow margin outside the border. It draws but takes no input.</param>
/// <param name="Border">The width of the left, right and bottom borders.</param>
/// <param name="Titlebar">The height of the titlebar above the content.</param>
public readonly record struct UIStripFrameMetrics(int Margin, int Border, int Titlebar)
{
    /// <summary>
    /// Gets the horizontal distance from the outer edge to the content.
    /// </summary>
    public int InsetX => Margin + Border;

    /// <summary>
    /// Gets the distance from the outer top edge to the content.
    /// </summary>
    public int InsetY => Margin + Titlebar;

    /// <summary>
    /// Gets the distance from the content to the outer bottom edge.
    /// </summary>
    public int InsetBottom => Margin + Border;
}
