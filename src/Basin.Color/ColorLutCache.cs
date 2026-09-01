using Basin.Capabilities;

namespace Basin.Color;

public sealed class ColorLutCache : IDisposable
{
    private bool _disposed;

    private readonly IRenderer _renderer;
    private readonly Dictionary<(ImageDescription Source, ImageDescription Output), IColorLut?> _luts;

    public ColorLutCache(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        _luts = new(PairComparer.Instance);
    }

    public IColorLut? LutFor(ImageDescription source, ImageDescription output)
    {
        if (_renderer.ColorTransform == ColorTransformCapability.None)
        {
            return null;
        }

        var key = (source, output);
        if (_luts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        IColorLut? imported = null;
        if (source.IccData is { } icc)
        {
            if (ColorLutBaker.BakeFromIcc(icc, output) is { } table)
            {
                imported = _renderer.ImportLut(table);
            }
        }
        else if (!ColorLutBaker.IsIdentity(source, output))
        {
            imported = _renderer.ImportLut(ColorLutBaker.Bake(source, output));
        }

        _luts[key] = imported;
        return imported;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var lut in _luts.Values)
        {
            lut?.Dispose();
        }

        _luts.Clear();
    }

    private sealed class PairComparer : IEqualityComparer<(ImageDescription Source, ImageDescription Output)>
    {
        public static PairComparer Instance { get; } = new();

        public bool Equals(
            (ImageDescription Source, ImageDescription Output) x,
            (ImageDescription Source, ImageDescription Output) y) =>
            ImageDescription.ContentComparer.Equals(x.Source, y.Source) &&
            ImageDescription.ContentComparer.Equals(x.Output, y.Output);

        public int GetHashCode((ImageDescription Source, ImageDescription Output) key) =>
            HashCode.Combine(
                ImageDescription.ContentComparer.GetHashCode(key.Source),
                ImageDescription.ContentComparer.GetHashCode(key.Output));
    }
}
