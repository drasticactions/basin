namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects how a theme part draws its background. The values are the uxtheme <c>BGTYPE</c> values.
/// </summary>
public enum BackgroundType
{
    /// <summary>
    /// The part draws its background from an image.
    /// </summary>
    ImageFile = 0,
    /// <summary>
    /// The part draws a border and a fill from the theme's colors.
    /// </summary>
    BorderFill = 1,
    /// <summary>
    /// The part draws no background.
    /// </summary>
    None = 2,
}
