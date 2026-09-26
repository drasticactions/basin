using Basin.UI.Quill;
using Prowl.Scribe;

namespace Basin.UI.Paper;

public static class PaperFonts
{
    public static FontFile? Resolve(Prowl.PaperUI.Paper paper)
    {
        ArgumentNullException.ThrowIfNull(paper);
        return QuillFonts.Resolve(paper.Canvas);
    }

    public static int AddFallbacks(Prowl.PaperUI.Paper paper, FontFile? primary)
    {
        ArgumentNullException.ThrowIfNull(paper);
        return QuillFonts.AddFallbacks(paper.Canvas, primary);
    }
}
