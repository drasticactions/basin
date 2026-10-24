namespace Basin.SoftKeyboard.Fcitx5;

/// <summary>
/// Holds one candidate page as fcitx5 sent it in UpdateCandidateArea.
/// </summary>
/// <param name="Words">The candidate words on the page, in display order.</param>
/// <param name="HasPrevious">A value indicating whether a previous page exists.</param>
/// <param name="HasNext">A value indicating whether a next page exists.</param>
/// <param name="Page">The page index that fcitx5 reports, or -1 when no page shows.</param>
/// <param name="Cursor">The index of the highlighted word, or -1 when no word is highlighted.</param>
public sealed record Fcitx5Candidates(IReadOnlyList<string> Words, bool HasPrevious, bool HasNext, int Page, int Cursor)
{
    /// <summary>
    /// The page with no words, no neighbor pages, and -1 for page and cursor.
    /// </summary>
    public static readonly Fcitx5Candidates Empty = new([], false, false, -1, -1);
}
