using SkiaSharp;

namespace DeskbarWm;

internal static class Fonts
{
    private static Basin.WindowManager.Skia.Fonts? _instance;

    public static SKTypeface Sans => Instance.Sans;

    public static string Ellipsize(SKFont font, string text, float maxWidth) =>
        Basin.WindowManager.Skia.Fonts.Ellipsize(font, text, maxWidth);

    private static Basin.WindowManager.Skia.Fonts Instance => _instance ??= Create();

    private static Basin.WindowManager.Skia.Fonts Create()
    {
        using var stream = typeof(Fonts).Assembly.GetManifestResourceStream("NotoSansCJK-Regular.ttc")
            ?? throw new InvalidOperationException("the embedded NotoSansCJK-Regular.ttc font is missing");
        return new Basin.WindowManager.Skia.Fonts(stream, "Noto Sans CJK JP");
    }
}
