using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Basin.UI.Skia;

public sealed class SkiaShapedTextCache : IDisposable
{
    private const int MaxEntries = 128;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Dictionary<string, (SKTextBlob Blob, float Width)> _blobs = [];
    private readonly Dictionary<(string Text, float MaxWidth), (SKTextBlob Blob, float Width)> _bounded = [];
    private readonly SKShaper _shaper;
    private bool _disposed;

    public SkiaShapedTextCache(SKTypeface typeface)
    {
        _shaper = new SKShaper(typeface);
        BasinCounters.Track();
    }

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
