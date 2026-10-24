using Basin.Diagnostics;
using Prowl.Quill;
using Prowl.Scribe;

namespace Basin.UI.Quill;

/// <summary>
/// Picks system fonts for a Quill canvas.
/// </summary>
public static class QuillFonts
{
    private static readonly BasinLogger Log = BasinLog.For("quill");

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

    /// <summary>
    /// Adds one CJK font and one emoji font from the system fonts as fallbacks of the canvas. A font that cannot be read is skipped.
    /// </summary>
    /// <param name="canvas">The canvas that gets the fallbacks.</param>
    /// <param name="primary">The primary font, which is never added as a fallback, or <see langword="null"/>.</param>
    /// <returns>The number of fonts added, from 0 to 2.</returns>
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
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Debug(error, $"fonts: system font scan stopped early");
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

    /// <summary>
    /// Picks the primary user interface font from the system fonts: Cantarell, DejaVu Sans, Noto Sans or Liberation Sans, in that order. The regular style wins.
    /// </summary>
    /// <param name="canvas">The canvas that lists the system fonts.</param>
    /// <returns>The font, the first system font when none of these is present, or <see langword="null"/> when there are no system fonts.</returns>
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
