using Basin.Render.Skia;
using SkiaSharp;

namespace EightWm;

internal static class Fonts
{
    private static SKTypeface? _regular;
    private static SKTypeface? _semibold;
    private static SKTypeface? _configured;

    public static SKTypeface Regular => _configured ?? Bundled(ref _regular, "Selawik-Regular.ttf", "Selawik");

    public static SKTypeface Semibold =>
        _configured ?? Bundled(ref _semibold, "Selawik-Semibold.ttf", "Selawik Semibold");

    public static void SetConfigured(string? spec)
    {
        if (_configured is { } previous)
        {
            SkiaCensus.Release(previous);
            _configured = null;
        }

        if (string.IsNullOrEmpty(spec))
        {
            return;
        }

        if (spec.Contains('/') && File.Exists(spec))
        {
            var fromFile = SKTypeface.FromFile(spec);
            _configured = fromFile is null ? null : SkiaCensus.Track(fromFile);
            return;
        }

        var byName = SKTypeface.FromFamilyName(spec);
        if (byName is not null && byName.FamilyName == spec)
        {
            _configured = SkiaCensus.Track(byName);
        }
        else
        {
            byName?.Dispose();
        }
    }

    public static void Release()
    {
        SkiaCensus.Release(_configured);
        SkiaCensus.Release(_regular);
        SkiaCensus.Release(_semibold);
        _configured = null;
        _regular = null;
        _semibold = null;
    }

    public static string Ellipsize(SKFont font, string text, float maxWidth)
    {
        if (text.Length == 0 || font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        var fit = (int)font.BreakText(text, Math.Max(maxWidth - font.MeasureText("…"), 0));
        return fit <= 0 ? "…" : text[..fit] + "…";
    }

    private static SKTypeface Bundled(ref SKTypeface? slot, string resource, string family)
    {
        if (slot is not null)
        {
            return slot;
        }

        using var stream = typeof(Fonts).Assembly.GetManifestResourceStream(resource);
        if (stream is not null)
        {
            using var data = SKData.Create(stream);
            var face = SKTypeface.FromData(data);
            if (face is not null && face.FamilyName == family)
            {
                slot = SkiaCensus.Track(face);
                return slot;
            }

            if (face is not null)
            {
                slot = SkiaCensus.Track(face);
                return slot;
            }
        }

        slot = SkiaCensus.Track(SKTypeface.FromFamilyName("sans-serif") ?? SKTypeface.Default);
        return slot;
    }
}
