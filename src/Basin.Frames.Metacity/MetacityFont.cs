using Basin.Render.Skia;
using Basin.UI.Skia;
using SkiaSharp;

namespace Basin.Frames.Metacity;

/// <summary>
/// The title typeface at a base size, with one Skia font and one shaped-text cache for each title scale.
/// </summary>
public sealed class MetacityFont : IDisposable
{
    private readonly List<(double Scale, SKFont Font, SkiaShapedTextCache Cache, int TextHeight)> _scaled = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetacityFont"/> class.
    /// </summary>
    /// <param name="typeface">The title typeface. This instance does not own it.</param>
    /// <param name="size">The base size in logical pixels. A theme's <c>title_scale</c> multiplies it.</param>
    public MetacityFont(SKTypeface typeface, float size)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        Typeface = typeface;
        Size = size;
    }

    /// <summary>
    /// Gets the title typeface.
    /// </summary>
    public SKTypeface Typeface { get; }

    /// <summary>
    /// Gets the base size in logical pixels.
    /// </summary>
    public float Size { get; }

    /// <summary>
    /// Gets the text height at a title scale, the font's descent less its ascent, rounded.
    /// </summary>
    /// <param name="titleScale">The theme's title scale.</param>
    /// <returns>The height in logical pixels.</returns>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public int TextHeight(double titleScale) => Scaled(titleScale).TextHeight;

    internal SKFont FontFor(double titleScale) => Scaled(titleScale).Font;

    internal SkiaShapedTextCache CacheFor(double titleScale) => Scaled(titleScale).Cache;

    private (double Scale, SKFont Font, SkiaShapedTextCache Cache, int TextHeight) Scaled(double titleScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var existing in _scaled)
        {
            if (existing.Scale == titleScale)
            {
                return existing;
            }
        }

        var size = Math.Max((float)(titleScale * Size), 1f);
        var font = SkiaCensus.Track(new SKFont(Typeface, size) { Subpixel = true });
        var metrics = font.Metrics;
        var height = (int)Math.Round(metrics.Descent - metrics.Ascent, MidpointRounding.AwayFromZero);
        var entry = (titleScale, font, new SkiaShapedTextCache(Typeface), height);
        _scaled.Add(entry);
        return entry;
    }

    /// <summary>
    /// Releases the fonts and text caches. It does not dispose <see cref="Typeface"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _scaled)
        {
            entry.Cache.Dispose();
            SkiaCensus.Release(entry.Font);
        }

        _scaled.Clear();
    }
}
