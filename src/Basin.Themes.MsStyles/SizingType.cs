namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects how an image background fills a destination of a different size. The values are the uxtheme <c>SIZINGTYPE</c> values.
/// </summary>
public enum SizingType
{
    /// <summary>
    /// The image draws at its own size and does not stretch.
    /// </summary>
    TrueSize = 0,
    /// <summary>
    /// The image stretches to the destination, with fixed sizing margins.
    /// </summary>
    Stretch = 1,
    /// <summary>
    /// The image tiles over the destination, with fixed sizing margins.
    /// </summary>
    Tile = 2,
}
