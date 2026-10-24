namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects how a border-fill background fills its interior. The values are the uxtheme <c>FILLTYPE</c> values.
/// </summary>
public enum FillType
{
    /// <summary>
    /// The interior is one solid color.
    /// </summary>
    Solid = 0,
    /// <summary>
    /// The interior is a gradient from top to bottom.
    /// </summary>
    VerticalGradient = 1,
    /// <summary>
    /// The interior is a gradient from left to right.
    /// </summary>
    HorizontalGradient = 2,
    /// <summary>
    /// The interior is a gradient from the center outward.
    /// </summary>
    RadialGradient = 3,
    /// <summary>
    /// The interior is a tiled image.
    /// </summary>
    TileImage = 4,
}
