using Basin.Render.Skia;
using Basin.UI.Skia;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

/// <summary>
/// Holds the fonts, paints and colors that the Skia prompt views draw with. The paints are shared, so a view sets their color before each draw.
/// </summary>
public sealed class SkiaPromptTheme : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaPromptTheme"/> class and makes the fonts and paints.
    /// </summary>
    /// <param name="typeface">The typeface for all text, or <see langword="null"/> for the default typeface. The caller keeps ownership.</param>
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

    /// <summary>
    /// Gets the typeface that all fonts use.
    /// </summary>
    public SKTypeface Typeface { get; }

    /// <summary>
    /// Gets the 16-point bold font for dialog titles.
    /// </summary>
    public SKFont TitleFont { get; }

    /// <summary>
    /// Gets the 13-point font for rows and buttons.
    /// </summary>
    public SKFont BodyFont { get; }

    /// <summary>
    /// Gets the 11-point font for secondary text.
    /// </summary>
    public SKFont SmallFont { get; }

    /// <summary>
    /// Gets the cache of shaped text blobs that <see cref="DrawText"/> and <see cref="MeasureText"/> use.
    /// </summary>
    public SkiaShapedTextCache Text { get; }

    /// <summary>
    /// Gets the shared antialiased fill paint.
    /// </summary>
    public SKPaint Fill { get; }

    /// <summary>
    /// Gets the shared antialiased stroke paint, 1 pixel wide.
    /// </summary>
    public SKPaint Stroke { get; }

    /// <summary>
    /// Gets the color behind a dialog.
    /// </summary>
    public SKColor Backdrop { get; init; } = new(0x1E, 0x20, 0x26, 0xF2);

    /// <summary>
    /// Gets the fill color of the dialog panel and the area hint.
    /// </summary>
    public SKColor Panel { get; init; } = new(0x2A, 0x2D, 0x35);

    /// <summary>
    /// Gets the color of the panel border and the checkbox borders.
    /// </summary>
    public SKColor Outline { get; init; } = new(0x0A, 0x0B, 0x0D);

    /// <summary>
    /// Gets the color of the main text.
    /// </summary>
    public SKColor Foreground { get; init; } = new(0xDE, 0xE1, 0xE6);

    /// <summary>
    /// Gets the color of secondary text and empty values.
    /// </summary>
    public SKColor Muted { get; init; } = new(0x9A, 0x9E, 0xA6);

    /// <summary>
    /// Gets the color of a checked checkbox and of a row that captures a chord.
    /// </summary>
    public SKColor Accent { get; init; } = new(0x5B, 0x7B, 0xA8);

    /// <summary>
    /// Gets the fill color of the row under the pointer.
    /// </summary>
    public SKColor RowHot { get; init; } = new(0x3A, 0x3E, 0x47);

    /// <summary>
    /// Gets the fill color of a selected row.
    /// </summary>
    public SKColor RowSelected { get; init; } = new(0x3C, 0x50, 0x6E);

    /// <summary>
    /// Gets the fill color of the primary button.
    /// </summary>
    public SKColor ButtonPrimary { get; init; } = new(0x4F, 0x7A, 0xC0);

    /// <summary>
    /// Gets the fill color of the primary button under the pointer, and the outline of an area selection.
    /// </summary>
    public SKColor ButtonPrimaryHot { get; init; } = new(0x5E, 0x8A, 0xD2);

    /// <summary>
    /// Gets the fill color of a secondary button and of an unchecked checkbox.
    /// </summary>
    public SKColor ButtonSecondary { get; init; } = new(0x3A, 0x3E, 0x47);

    /// <summary>
    /// Gets the fill color of a secondary button under the pointer.
    /// </summary>
    public SKColor ButtonSecondaryHot { get; init; } = new(0x4A, 0x4F, 0x5A);

    /// <summary>
    /// Gets the translucent color that an area prompt lays over the output.
    /// </summary>
    public SKColor Veil { get; init; } = new(0x00, 0x00, 0x00, 0x60);

    /// <summary>
    /// Gets the translucent fill color of an area selection.
    /// </summary>
    public SKColor Rubber { get; init; } = new(0x5E, 0x8A, 0xD2, 0x60);

    /// <summary>
    /// Draws one line of shaped text with the shared fill paint. Text wider than <paramref name="maxWidth"/> is clipped, not wrapped.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="text">The text. An empty string draws nothing.</param>
    /// <param name="font">The font to draw with.</param>
    /// <param name="x">The left edge in logical pixels.</param>
    /// <param name="baseline">The baseline position in logical pixels.</param>
    /// <param name="color">The text color.</param>
    /// <param name="maxWidth">The widest that the text can be, in logical pixels.</param>
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

    /// <summary>
    /// Measures the advance width of one line of shaped text.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="font">The font to measure with.</param>
    /// <returns>The width in logical pixels, or 0 for empty text or text that does not shape.</returns>
    public float MeasureText(string text, SKFont font) =>
        !string.IsNullOrEmpty(text) && Text.TryGetBlob(text, font, out _, out var width) ? width : 0;

    /// <summary>
    /// Releases the text cache, the fonts and the paints. It does not release the typeface.
    /// </summary>
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
