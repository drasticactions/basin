namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects how a font glyph picks its size. The values are the uxtheme <c>GLYPHFONTSIZINGTYPE</c> values.
/// </summary>
public enum GlyphFontSizingType
{
    /// <summary>
    /// The glyph font keeps its theme size.
    /// </summary>
    None = 0,
    /// <summary>
    /// The glyph font scales with the size of the part.
    /// </summary>
    Size = 1,
    /// <summary>
    /// The glyph font scales with the output DPI.
    /// </summary>
    Dpi = 2,
}
