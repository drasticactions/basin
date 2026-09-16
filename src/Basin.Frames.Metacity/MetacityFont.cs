using Basin.Render.Skia;
using Basin.UI.Skia;
using SkiaSharp;

namespace Basin.Frames.Metacity;

public sealed class MetacityFont : IDisposable
{
    private readonly List<(double Scale, SKFont Font, SkiaShapedTextCache Cache, int TextHeight)> _scaled = [];
    private bool _disposed;

    public MetacityFont(SKTypeface typeface, float size)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        Typeface = typeface;
        Size = size;
    }

    public SKTypeface Typeface { get; }

    public float Size { get; }

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
