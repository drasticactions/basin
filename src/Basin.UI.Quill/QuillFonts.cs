using Prowl.Quill;
using Prowl.Scribe;

namespace Basin.UI.Quill;

public static class QuillFonts
{
    private static readonly string[] Preferred =
    [
        "Cantarell",
        "DejaVu Sans",
        "Noto Sans",
        "Liberation Sans",
    ];

    private static readonly string[] PreferredCjk =
    [
        "Noto Sans CJK JP",
        "Noto Sans CJK",
        "Noto Sans JP",
        "Source Han Sans",
        "Droid Sans Fallback",
        "WenQuanYi Zen Hei",
    ];

    private static readonly string[] PreferredEmoji =
    [
        "Noto Color Emoji",
        "Noto Emoji",
        "Twemoji",
        "OpenMoji",
    ];

    public static int AddFallbacks(Canvas canvas, FontFile? primary)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        FontFile? cjk = null;
        FontFile? emoji = null;
        try
        {
            foreach (var font in canvas.EnumerateSystemFonts())
            {
                if (primary is not null && ReferenceEquals(font, primary))
                {
                    continue;
                }

                cjk ??= Best(font, PreferredCjk);
                emoji ??= Best(font, PreferredEmoji);
                if (cjk is not null && emoji is not null)
                {
                    break;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var added = 0;
        if (cjk is not null)
        {
            canvas.AddFallbackFont(cjk);
            added++;
        }

        if (emoji is not null)
        {
            canvas.AddFallbackFont(emoji);
            added++;
        }

        return added;
    }

    private static FontFile? Best(FontFile font, string[] wanted)
    {
        for (var i = 0; i < wanted.Length; i++)
        {
            if (font.FamilyName.Contains(wanted[i], StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }
        }

        return null;
    }

    public static FontFile? Resolve(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var matches = new FontFile?[Preferred.Length];
        FontFile? first = null;
        try
        {
            foreach (var font in canvas.EnumerateSystemFonts())
            {
                first ??= font;
                for (var i = 0; i < Preferred.Length; i++)
                {
                    if (!string.Equals(font.FamilyName, Preferred[i], StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (matches[i] is null || (matches[i]!.Style != FontStyle.Regular && font.Style == FontStyle.Regular))
                    {
                        matches[i] = font;
                    }
                }
            }
        }
        catch (IOException)
        {
            return first;
        }
        catch (UnauthorizedAccessException)
        {
            return first;
        }

        foreach (var match in matches)
        {
            if (match is not null)
            {
                return match;
            }
        }

        return first;
    }
}
