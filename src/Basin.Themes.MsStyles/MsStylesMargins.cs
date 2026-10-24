namespace Basin.Themes.MsStyles;

/// <summary>
/// Holds the four margins of a theme property, for example the sizing margins of an image. The values are in image pixels.
/// </summary>
/// <param name="Left">The left margin.</param>
/// <param name="Right">The right margin.</param>
/// <param name="Top">The top margin.</param>
/// <param name="Bottom">The bottom margin.</param>
public readonly record struct MsStylesMargins(int Left, int Right, int Top, int Bottom);
