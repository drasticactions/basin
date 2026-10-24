using Basin.Themes.MsStyles;

namespace Basin.Frames.MsStyles;

/// <summary>
/// Measures title text for the frame layout. The glass look uses it to size the glow plate behind the title.
/// </summary>
public interface IMsStylesTextMetrics
{
    /// <summary>
    /// Measures the width of a text in one line, with no ellipsis.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="font">The theme font.</param>
    /// <param name="pixels">The font size in pixels.</param>
    /// <returns>The width in the same pixel unit as <paramref name="pixels"/>.</returns>
    float Measure(string text, in MsStylesFont font, float pixels);
}
