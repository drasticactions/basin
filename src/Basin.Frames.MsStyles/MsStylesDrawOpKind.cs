namespace Basin.Frames.MsStyles;

/// <summary>
/// The kinds of <see cref="MsStylesDrawOp"/>.
/// </summary>
public enum MsStylesDrawOpKind
{
    /// <summary>
    /// Draws a part of a theme bitmap, stretched or tiled.
    /// </summary>
    Image,
    /// <summary>
    /// Fills a rectangle with one color.
    /// </summary>
    Fill,
    /// <summary>
    /// Strokes a rectangle, rounded rectangle or ellipse inside the destination.
    /// </summary>
    Border,
    /// <summary>
    /// Fills a shape with a gradient or a solid color.
    /// </summary>
    Gradient,
    /// <summary>
    /// Draws one line of text, cut with an ellipsis, with an optional shadow.
    /// </summary>
    Text,
    /// <summary>
    /// Draws a blurred copy of a text as a glow behind it.
    /// </summary>
    Glow,
    /// <summary>
    /// Draws the window icon, from client pixels or from a name.
    /// </summary>
    Icon,
    /// <summary>
    /// Saves the painter state and clips to the destination until the matching <see cref="Restore"/>.
    /// </summary>
    Clip,
    /// <summary>
    /// Restores the state that the last <see cref="Clip"/> saved.
    /// </summary>
    Restore,
}
