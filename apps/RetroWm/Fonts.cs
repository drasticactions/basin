using SkiaSharp;

namespace RetroWm;

internal static class Fonts
{
    private static SKTypeface? _bundled;
    private static SKTypeface? _configured;

    public static SKTypeface Sans => _configured ?? Bundled;

    public static void SetConfigured(string? spec)
    {
        if (_configured is { } previous && !ReferenceEquals(previous, _bundled))
        {
            previous.Dispose();
        }

        _configured = null;
        if (string.IsNullOrEmpty(spec))
        {
            return;
        }

        if (spec.Contains('/') && File.Exists(spec))
        {
            _configured = SKTypeface.FromFile(spec);
            return;
        }

        var byName = SKTypeface.FromFamilyName(spec);
        if (byName is not null && byName.FamilyName == spec)
        {
            _configured = byName;
        }
        else
        {
            byName?.Dispose();
        }
    }

    public static string Ellipsize(SKFont font, string text, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        var fit = (int)font.BreakText(text, Math.Max(maxWidth - font.MeasureText("…"), 0));
        return fit <= 0 ? "…" : text[..fit] + "…";
    }

    private static SKTypeface Bundled
    {
        get
        {
            if (_bundled is not null)
            {
                return _bundled;
            }

            using var stream = typeof(Fonts).Assembly.GetManifestResourceStream("NotoSansCJK-Regular.ttc")
                ?? throw new InvalidOperationException("the embedded NotoSansCJK-Regular.ttc font is missing");
            using var data = SKData.Create(stream);
            for (var index = 0; ; index++)
            {
                var face = SKTypeface.FromData(data, index);
                if (face is null)
                {
                    break;
                }

                if (face.FamilyName == "Noto Sans CJK JP")
                {
                    _bundled = face;
                    return face;
                }

                face.Dispose();
            }

            _bundled = SKTypeface.FromData(data, 0) ?? SKTypeface.Default;
            return _bundled;
        }
    }
}
