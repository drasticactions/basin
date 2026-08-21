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

    public void Clear()
    {
        _thread.Assert();
        foreach (var entry in _blobs.Values)
        {
            SkiaCensus.Release(entry.Blob);
        }

        _blobs.Clear();
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
