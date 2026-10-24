namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects the kind of glyph that a part draws over its background. The values are the uxtheme <c>GLYPHTYPE</c> values.
/// </summary>
public enum GlyphType
{
    /// <summary>
    /// The part draws no glyph.
    /// </summary>
    None = 0,
    /// <summary>
    /// The glyph is an image.
    /// </summary>
    ImageGlyph = 1,
    /// <summary>
    /// The glyph is a character from a font.
    /// </summary>
    FontGlyph = 2,
}
