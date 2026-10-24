namespace Basin.SoftKeyboard;

/// <summary>
/// One category of the emoji set that <see cref="EmojiPages"/> lays out as pages.
/// </summary>
/// <param name="Id">The category id, used in the page names, for example <c>emoji-faces-2</c>.</param>
/// <param name="Icon">The text on the category key in the bottom row of each emoji page.</param>
/// <param name="Emoji">The emoji of the category, in page order. Each entry is typed as one commit.</param>
public sealed record EmojiCategory(string Id, string Icon, IReadOnlyList<string> Emoji);
