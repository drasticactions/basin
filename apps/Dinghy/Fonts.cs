using SkiaSharp;
using Tomlyn.Model;

namespace Dinghy;

internal static class Fonts
{
    public static SKTypeface Sans { get; } = SKTypeface.FromFamilyName("sans-serif") ?? SKTypeface.Default;

    public static string Ellipsize(SKFont font, string text, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        var fit = (int)font.BreakText(text, Math.Max(maxWidth - font.MeasureText("…"), 0));
        return fit <= 0 ? "…" : text[..fit] + "…";
    }
}
