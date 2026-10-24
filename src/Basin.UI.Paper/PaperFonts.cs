using Basin.UI.Quill;
using Prowl.Scribe;

namespace Basin.UI.Paper;

/// <summary>
/// Picks system fonts for the canvas of a Paper instance.
/// </summary>
public static class PaperFonts
{
    /// <summary>
    /// Picks the primary user interface font from the system fonts that the canvas lists.
    /// </summary>
    /// <param name="paper">The Paper instance.</param>
    /// <returns>The font, or <see langword="null"/> when there are no system fonts.</returns>
    public static FontFile? Resolve(Prowl.PaperUI.Paper paper)
    {
        ArgumentNullException.ThrowIfNull(paper);
        return QuillFonts.Resolve(paper.Canvas);
    }

    /// <summary>
    /// Adds one CJK font and one emoji font from the system fonts as fallbacks of the canvas.
    /// </summary>
    /// <param name="paper">The Paper instance.</param>
    /// <param name="primary">The primary font, which is never added as a fallback, or <see langword="null"/>.</param>
    /// <returns>The number of fonts added, from 0 to 2.</returns>
    public static int AddFallbacks(Prowl.PaperUI.Paper paper, FontFile? primary)
    {
        ArgumentNullException.ThrowIfNull(paper);
        return QuillFonts.AddFallbacks(paper.Canvas, primary);
    }
}
