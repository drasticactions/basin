using Basin.Render.Skia;
using SkiaSharp;

namespace Basin.UI.Skia;

/// <summary>
/// The fonts, paints, text caches and icons that a <see cref="FlatFrameRenderer"/> draws with. Use it on one thread.
/// </summary>
public sealed class FlatFrameTheme : IDisposable
{
    private const string BundledFont = "Selawik-Semibold.ttf";

    private readonly bool _ownsTypeface;
    private readonly Dictionary<IBuffer, SKImage?> _pixelIcons = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlatFrameTheme"/> class.
    /// </summary>
    /// <param name="typeface">The typeface of the title and the badges.</param>
    /// <param name="fontSize">The title size in logical pixels. The badge font is 11/14 of it.</param>
    /// <param name="ownsTypeface"><see langword="true"/> to release the typeface in <see cref="Dispose"/>.</param>
    public FlatFrameTheme(SKTypeface typeface, float fontSize = 13, bool ownsTypeface = false)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        Typeface = typeface;
        _ownsTypeface = ownsTypeface;
        FontSize = fontSize;
        TitleFont = SkiaCensus.Track(new SKFont(typeface, fontSize) { Subpixel = true });
        BadgeFont = SkiaCensus.Track(new SKFont(typeface, MathF.Round(fontSize * 11 / 14)) { Subpixel = true });
        Text = new SkiaShapedTextCache(typeface);
        Badges = new SkiaShapedTextCache(typeface);
        Fill = SkiaCensus.Track(new SKPaint { IsAntialias = true });
        Stroke = SkiaCensus.Track(new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        });
    }

    /// <summary>
    /// Makes a theme with the bundled Selawik Semibold font, which the theme owns.
    /// </summary>
    /// <param name="fontSize">The title size in logical pixels.</param>
    /// <returns>The theme. The caller must dispose it.</returns>
    /// <exception cref="InvalidOperationException">The bundled font is missing or does not load.</exception>
    public static FlatFrameTheme Default(float fontSize = 13)
    {
        using var stream = typeof(FlatFrameTheme).Assembly.GetManifestResourceStream(BundledFont)
            ?? throw new InvalidOperationException($"Basin.UI.Skia is missing {BundledFont}.");
        using var data = SKData.Create(stream);
        var typeface = SkiaCensus.Track(SKTypeface.FromData(data))
            ?? throw new InvalidOperationException($"{BundledFont} did not load.");
        return new FlatFrameTheme(typeface, fontSize, ownsTypeface: true);
    }

    /// <summary>
    /// Gets the title size in logical pixels.
    /// </summary>
    public float FontSize { get; }

    /// <summary>
    /// Gets the typeface of the title and the badges.
    /// </summary>
    public SKTypeface Typeface { get; }

    /// <summary>
    /// Gets the font of the title and the menu items.
    /// </summary>
    public SKFont TitleFont { get; }

    /// <summary>
    /// Gets the font of the initial in a badge icon.
    /// </summary>
    public SKFont BadgeFont { get; }

    /// <summary>
    /// Gets the shaped text cache for <see cref="TitleFont"/>.
    /// </summary>
    public SkiaShapedTextCache Text { get; }

    /// <summary>
    /// Gets the shaped text cache for <see cref="BadgeFont"/>.
    /// </summary>
    public SkiaShapedTextCache Badges { get; }

    /// <summary>
    /// Gets the shared antialiased fill paint. A draw sets its color before use.
    /// </summary>
    public SKPaint Fill { get; }

    /// <summary>
    /// Gets the shared antialiased stroke paint with round caps. A draw sets its color and width before use.
    /// </summary>
    public SKPaint Stroke { get; }

    /// <summary>
    /// Gets or sets the icon lookup that takes an icon name and an integer scale. When it is <see langword="null"/>, frames show a badge with the initial of the app.
    /// </summary>
    public Func<string, int, SKImage?>? Icons { get; set; }

    /// <summary>
    /// Looks up a named icon through <see cref="Icons"/>.
    /// </summary>
    /// <param name="name">The icon name or the app id.</param>
    /// <param name="scale">The integer scale of the icon.</param>
    /// <returns>The image, or <see langword="null"/> when there is no lookup or no icon.</returns>
    public SKImage? IconFor(string name, int scale) => Icons?.Invoke(name, scale);

    /// <summary>
    /// Gets an image of a client icon buffer. The theme copies the pixels on the first call for each buffer and keeps the copy until the buffer is destroyed.
    /// </summary>
    /// <param name="buffer">The Argb8888 icon buffer.</param>
    /// <returns>The image that the theme owns, or <see langword="null"/> when the buffer has no CPU path or no Skia layout.</returns>
    public SKImage? ImageFor(IBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (_pixelIcons.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        SKImage? image = null;
        if (SkiaRenderer.TryImageInfo(buffer.Width, buffer.Height, DrmFormat.Argb8888, out var info) &&
            buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            var copied = SKImage.FromPixelCopy(info, view.Data, view.Stride);
            buffer.EndDataAccess();
            if (copied is not null)
            {
                image = SkiaCensus.Track(copied);
            }
        }

        _pixelIcons[buffer] = image;
        buffer.Destroyed += () =>
        {
            if (_pixelIcons.Remove(buffer, out var dead) && dead is not null)
            {
                SkiaCensus.Release(dead);
            }
        };
        return image;
    }

    /// <summary>
    /// Releases the fonts, the paints, the caches and the icon copies, and the typeface when the theme owns it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var icon in _pixelIcons.Values)
        {
            if (icon is not null)
            {
                SkiaCensus.Release(icon);
            }
        }

        _pixelIcons.Clear();
        Text.Dispose();
        Badges.Dispose();
        SkiaCensus.Release(Fill);
        SkiaCensus.Release(Stroke);
        SkiaCensus.Release(TitleFont);
        SkiaCensus.Release(BadgeFont);
        if (_ownsTypeface)
        {
            SkiaCensus.Release(Typeface);
        }
    }
}
