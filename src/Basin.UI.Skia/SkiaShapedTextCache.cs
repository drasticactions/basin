using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Basin.UI.Skia;

/// <summary>
/// Shapes text with HarfBuzz into Skia text blobs and keeps them. The key is the text only, so use one cache for each font. Use it on the thread that created it.
/// </summary>
public sealed class SkiaShapedTextCache : IDisposable
{
    private const int MaxEntries = 128;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Dictionary<string, (SKTextBlob Blob, float Width)> _blobs = [];
    private readonly Dictionary<(string Text, float MaxWidth), (SKTextBlob Blob, float Width)> _bounded = [];
    private readonly SKShaper _shaper;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaShapedTextCache"/> class.
    /// </summary>
    /// <param name="typeface">The typeface that the shaper uses.</param>
    public SkiaShapedTextCache(SKTypeface typeface)
    {
        _shaper = new SKShaper(typeface);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the shaped blob of a text. The font is used only when the text is not in the cache yet. The cache clears itself at 128 entries.
    /// </summary>
    /// <param name="text">The text to shape.</param>
    /// <param name="font">The font to shape with.</param>
    /// <param name="blob">The blob that the cache owns. It stays valid until the next call, <see cref="Clear"/> or <see cref="Dispose"/>.</param>
    /// <param name="width">The advance width of the text, in the units of the canvas.</param>
    /// <returns><see langword="false"/> when the text is empty or gives no glyphs.</returns>
    /// <exception cref="ObjectDisposedException">The cache is disposed.</exception>
    public bool TryGetBlob(string text, SKFont font, out SKTextBlob blob, out float width)
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_blobs.TryGetValue(text, out var entry))
        {
            (blob, width) = entry;
            return true;
        }

        if (text.Length == 0)
        {
            blob = null!;
            width = 0;
            return false;
        }

        var result = _shaper.Shape(text, font);
        if (result.Codepoints.Length == 0)
        {
            blob = null!;
            width = 0;
            return false;
        }

        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocatePositionedRun(font, result.Codepoints.Length);
        var glyphs = run.Glyphs;
        var positions = run.Positions;
        for (var i = 0; i < result.Codepoints.Length; i++)
        {
            glyphs[i] = (ushort)result.Codepoints[i];
            positions[i] = result.Points[i];
        }

        var built = builder.Build();
        if (built is null)
        {
            blob = null!;
            width = 0;
            return false;
        }

        if (_blobs.Count >= MaxEntries)
        {
            Clear();
        }

        _blobs[text] = (SkiaCensus.Track(built), result.Width);
        (blob, width) = (built, result.Width);
        return true;
    }

    /// <summary>
    /// Gets the shaped blob of a text that fits a width. A text that is too wide is cut at a grapheme boundary and ends with an ellipsis.
    /// </summary>
    /// <param name="text">The text to shape.</param>
    /// <param name="font">The font to shape with.</param>
    /// <param name="maxWidth">The largest advance width that the blob can have.</param>
    /// <param name="blob">The blob that the cache owns. It stays valid until the next call, <see cref="Clear"/> or <see cref="Dispose"/>.</param>
    /// <param name="width">The advance width of the blob.</param>
    /// <returns><see langword="false"/> when the text gives no glyphs, or not even the ellipsis fits.</returns>
    /// <exception cref="ObjectDisposedException">The cache is disposed.</exception>
    public bool TryGetBlob(string text, SKFont font, float maxWidth, out SKTextBlob blob, out float width)
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_bounded.TryGetValue((text, maxWidth), out var entry))
        {
            (blob, width) = entry;
            return true;
        }

        if (!TryGetBlob(text, font, out blob, out width))
        {
            return false;
        }

        if (width <= maxWidth)
        {
            return true;
        }

        var boundaries = GraphemeBoundaries(text);
        var low = 0;
        var high = boundaries.Count - 1;
        SKTextBlob? best = null;
        var bestWidth = 0f;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var candidate = string.Concat(text.AsSpan(0, boundaries[middle]), "\u2026");
            if (TryShape(candidate, font, out var shaped, out var shapedWidth) && shapedWidth <= maxWidth)
            {
                if (best is not null)
                {
                    best.Dispose();
                }

                best = shaped;
                bestWidth = shapedWidth;
                low = middle + 1;
            }
            else
            {
                shaped?.Dispose();
                high = middle - 1;
            }
        }

        if (best is null)
        {
            blob = null!;
            width = 0;
            return false;
        }

        if (_bounded.Count >= MaxEntries)
        {
            foreach (var bounded in _bounded.Values)
            {
                SkiaCensus.Release(bounded.Blob);
            }

            _bounded.Clear();
        }

        _bounded[(text, maxWidth)] = (SkiaCensus.Track(best), bestWidth);
        (blob, width) = (best, bestWidth);
        return true;
    }

    private static List<int> GraphemeBoundaries(string text)
    {
        var boundaries = new List<int> { 0 };
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var end = enumerator.ElementIndex + enumerator.GetTextElement().Length;
            if (end < text.Length)
            {
                boundaries.Add(end);
            }
        }

        return boundaries;
    }

    private bool TryShape(string text, SKFont font, out SKTextBlob? blob, out float width)
    {
        blob = null;
        width = 0;
        if (text.Length == 0)
        {
            return false;
        }

        var result = _shaper.Shape(text, font);
        if (result.Codepoints.Length == 0)
        {
            return false;
        }

        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocatePositionedRun(font, result.Codepoints.Length);
        var glyphs = run.Glyphs;
        var positions = run.Positions;
        for (var i = 0; i < result.Codepoints.Length; i++)
        {
            glyphs[i] = (ushort)result.Codepoints[i];
            positions[i] = result.Points[i];
        }

        blob = builder.Build();
        width = result.Width;
        return blob is not null;
    }

    /// <summary>
    /// Releases every cached blob. A blob that a call returned earlier is not valid after this.
    /// </summary>
    public void Clear()
    {
        _thread.Assert();
        foreach (var entry in _blobs.Values)
        {
            SkiaCensus.Release(entry.Blob);
        }

        _blobs.Clear();
        foreach (var entry in _bounded.Values)
        {
            SkiaCensus.Release(entry.Blob);
        }

        _bounded.Clear();
    }

    /// <summary>
    /// Releases the cached blobs and the shaper.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
        _shaper.Dispose();
        BasinCounters.Untrack();
    }
}
