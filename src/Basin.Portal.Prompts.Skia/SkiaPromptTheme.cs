using Basin.Render.Skia;
using Basin.UI.Skia;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

public sealed class SkiaPromptTheme : IDisposable
{
    public SkiaPromptTheme(SKTypeface? typeface = null)
    {
        Typeface = typeface ?? SKTypeface.Default;
        TitleFont = SkiaCensus.Track(new SKFont(Typeface, 16) { Subpixel = true, Embolden = true });
        BodyFont = SkiaCensus.Track(new SKFont(Typeface, 13) { Subpixel = true });
        SmallFont = SkiaCensus.Track(new SKFont(Typeface, 11) { Subpixel = true });
        Text = new SkiaShapedTextCache(Typeface);
        Fill = SkiaCensus.Track(new SKPaint { IsAntialias = true });
        Stroke = SkiaCensus.Track(new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 });
    }

    public SKTypeface Typeface { get; }

    public SKFont TitleFont { get; }

    public SKFont BodyFont { get; }

    public SKFont SmallFont { get; }

    public SkiaShapedTextCache Text { get; }

    public SKPaint Fill { get; }

    public SKPaint Stroke { get; }

    public SKColor Backdrop { get; init; } = new(0x1E, 0x20, 0x26, 0xF2);

    public SKColor Panel { get; init; } = new(0x2A, 0x2D, 0x35);

    public SKColor Outline { get; init; } = new(0x0A, 0x0B, 0x0D);

    public SKColor Foreground { get; init; } = new(0xDE, 0xE1, 0xE6);

    public SKColor Muted { get; init; } = new(0x9A, 0x9E, 0xA6);

    public SKColor Accent { get; init; } = new(0x5B, 0x7B, 0xA8);

    public SKColor RowHot { get; init; } = new(0x3A, 0x3E, 0x47);

    public SKColor RowSelected { get; init; } = new(0x3C, 0x50, 0x6E);

    public SKColor ButtonPrimary { get; init; } = new(0x4F, 0x7A, 0xC0);

    public SKColor ButtonPrimaryHot { get; init; } = new(0x5E, 0x8A, 0xD2);

    public SKColor ButtonSecondary { get; init; } = new(0x3A, 0x3E, 0x47);

    public SKColor ButtonSecondaryHot { get; init; } = new(0x4A, 0x4F, 0x5A);

    public SKColor Veil { get; init; } = new(0x00, 0x00, 0x00, 0x60);

    public SKColor Rubber { get; init; } = new(0x5E, 0x8A, 0xD2, 0x60);

    public void DrawText(SKCanvas canvas, string text, SKFont font, float x, float baseline, SKColor color, float maxWidth = float.MaxValue)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!Text.TryGetBlob(text, font, out var blob, out var width))
        {
            return;
        }

        Fill.Color = color;
        if (width > maxWidth && maxWidth > 0)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(x, baseline - font.Size * 1.5f, x + maxWidth, baseline + font.Size));
            canvas.DrawText(blob, x, baseline, Fill);
            canvas.Restore();
            return;
        }

        canvas.DrawText(blob, x, baseline, Fill);
    }

    public float MeasureText(string text, SKFont font) =>
        !string.IsNullOrEmpty(text) && Text.TryGetBlob(text, font, out _, out var width) ? width : 0;

    public void Dispose()
    {
        Text.Dispose();
        SkiaCensus.Release(TitleFont);
        SkiaCensus.Release(BodyFont);
        SkiaCensus.Release(SmallFont);
        SkiaCensus.Release(Fill);
        SkiaCensus.Release(Stroke);
    }
}
