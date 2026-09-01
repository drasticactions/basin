using SkiaSharp;

namespace Dinghy;

internal static class Fonts
{
    private static readonly Basin.WindowManager.Skia.Fonts Instance = new(fallbackFamily: "sans-serif");

    public static SKTypeface Sans => Instance.Sans;

    public static string Ellipsize(SKFont font, string text, float maxWidth) =>
        Basin.WindowManager.Skia.Fonts.Ellipsize(font, text, maxWidth);
}
