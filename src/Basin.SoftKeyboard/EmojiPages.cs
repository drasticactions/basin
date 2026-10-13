using Basin.Diagnostics;
using Tomlyn;
using Tomlyn.Model;

namespace Basin.SoftKeyboard;

public static class EmojiPages
{
    public const int MaxPerRow = 10;

    private static IReadOnlyList<EmojiCategory>? s_builtIn;

    public static IReadOnlyList<EmojiCategory> BuiltIn => s_builtIn ??= Load();

    public static string PageName(int category, int chunk) =>
        category == 0 && chunk == 0 ? SoftKeyboardEngine.EmojiPage : $"emoji-{category}-{chunk}";

    public static IReadOnlyList<EmojiCategory> Parse(string text, BasinLogger? log = null, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var logger = log ?? SoftKeyboardLog.Log;
        var table = Toml.ToModel(text, source);
        var categories = new List<EmojiCategory>();
        if (table.TryGetValue("category", out var raw) && raw is TomlTableArray array)
        {
            foreach (var entry in array)
            {
                var id = entry.TryGetValue("id", out var idValue) ? idValue as string : null;
                var icon = entry.TryGetValue("icon", out var iconValue) ? iconValue as string : null;
                if (id is null || icon is null || !entry.TryGetValue("emoji", out var list) || list is not TomlArray emoji)
                {
                    logger.Warn($"{source}: an emoji category needs id, icon and emoji, ignored");
                    continue;
                }

                categories.Add(new EmojiCategory(id, icon, [.. emoji.OfType<string>().Where(static e => e.Length > 0)]));
            }
        }

        return categories;
    }

    internal static Dictionary<string, KeyCell[][]> Build(int rows, int columns, IReadOnlyList<EmojiCategory> categories)
    {
        var pages = new Dictionary<string, KeyCell[][]>(StringComparer.Ordinal);
        var perRow = Math.Min(MaxPerRow, columns - 1);
        var contentRows = rows - 1;
        var shown = Math.Min(categories.Count, columns - 3);
        if (perRow < 1 || contentRows < 1 || shown < 1)
        {
            return pages;
        }

        var perPage = perRow * contentRows;
        for (var category = 0; category < shown; category++)
        {
            var emoji = categories[category].Emoji;
            var chunks = Math.Max(1, (emoji.Count + perPage - 1) / perPage);
            for (var chunk = 0; chunk < chunks; chunk++)
            {
                var page = new KeyCell[rows][];
                for (var row = 0; row < contentRows; row++)
                {
                    var cells = Blank(columns);
                    for (var column = 0; column < perRow; column++)
                    {
                        var index = (chunk * perPage) + (row * perRow) + column;
                        cells[column] = index < emoji.Count
                            ? new KeyCell(1, KeyCap.Character(emoji[index], null, false), null, null, KeyClass.Punctuation)
                            : KeyCell.Spacer(1);
                    }

                    cells[perRow] = row == 0
                        ? new KeyCell(1.5f, KeyCap.Backspace, null, null, KeyClass.Function)
                        : KeyCell.Spacer(1.5f);
                    page[row] = cells;
                }

                var bottom = Blank(columns);
                var at = 0;
                bottom[at++] = new KeyCell(1.5f, KeyCap.PageKey(KeyPageSet.MainPage, "abc"), null, null, KeyClass.Function);
                for (var other = 0; other < shown; other++)
                {
                    var cap = KeyCap.PageKey(PageName(other, 0), categories[other].Icon);
                    bottom[at++] = new KeyCell(1, cap, null, null, other == category ? KeyClass.Letter : KeyClass.Function);
                }

                var used = 1.5f + shown;
                if (chunks > 1)
                {
                    var next = KeyCap.PageKey(PageName(category, (chunk + 1) % chunks), "→");
                    bottom[at++] = new KeyCell(1, next, null, null, KeyClass.Function);
                    used += 1;
                }

                var fill = perRow + 1.5f - used;
                if (fill > 0)
                {
                    bottom[at] = new KeyCell(fill, KeyCap.Space, null, null, KeyClass.Letter);
                }

                page[rows - 1] = bottom;
                pages[PageName(category, chunk)] = page;
            }
        }

        return pages;
    }

    private static KeyCell[] Blank(int columns)
    {
        var cells = new KeyCell[columns];
        Array.Fill(cells, KeyCell.Empty);
        return cells;
    }

    private static IReadOnlyList<EmojiCategory> Load()
    {
        using var stream = typeof(EmojiPages).Assembly.GetManifestResourceStream("layouts/emoji.toml");
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), null, "layouts/emoji.toml");
    }
}
