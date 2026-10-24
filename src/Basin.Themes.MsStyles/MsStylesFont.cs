namespace Basin.Themes.MsStyles;

/// <summary>
/// Holds a font value of a theme property.
/// </summary>
/// <param name="Face">The font family name, for example <c>Segoe UI</c>.</param>
/// <param name="Points">The font size in points.</param>
/// <param name="Weight">The font weight, for example <see cref="Regular"/> or <see cref="Bold"/>.</param>
/// <param name="Italic">A value indicating whether the font is italic.</param>
/// <param name="Underline">A value indicating whether the font is underlined.</param>
/// <param name="StrikeOut">A value indicating whether the font has a line through it.</param>
public readonly record struct MsStylesFont(string Face, int Points, int Weight, bool Italic, bool Underline, bool StrikeOut)
{
    /// <summary>
    /// The weight of a regular font.
    /// </summary>
    public const int Regular = 400;

    /// <summary>
    /// The weight of a bold font.
    /// </summary>
    public const int Bold = 700;

    /// <summary>
    /// Converts the point size to pixels at a DPI.
    /// </summary>
    /// <param name="dpi">The output DPI. Use 96 for an output at scale 1.</param>
    /// <returns>The font size in pixels, <see cref="Points"/> times <paramref name="dpi"/> over 72.</returns>
    public double PixelSize(double dpi) => Points * dpi / 72.0;
}
