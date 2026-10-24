using Basin.Capabilities;

namespace Basin.Color;

/// <summary>
/// Resolves conversion tables for one renderer and caches them by description content. Only the
/// compositor thread can use it.
/// </summary>
public sealed class ColorLutCache : IColorTransformResolver, IDisposable, IRendererBound
{
    private bool _disposed;

    private IRenderer _renderer;
    private readonly Dictionary<(ImageDescription Source, ImageDescription Output), IColorLut?> _luts;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorLutCache"/> class.
    /// </summary>
    /// <param name="renderer">The renderer that imports every table.</param>
    public ColorLutCache(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        _luts = new(PairComparer.Instance);
    }

    /// <inheritdoc/>
    public ColorTransformCapability Capability => _renderer.ColorTransform;

    /// <summary>
    /// Gets the table that converts content from one description into another. The first call for a
    /// pair bakes and imports the table, and later calls return the same one.
    /// </summary>
    /// <param name="source">The description of the content.</param>
    /// <param name="output">The description of the space that the content composites into.</param>
    /// <returns>A table that the cache owns, or <see langword="null"/> when the renderer cannot import tables, the shaders convert directly, or no conversion is necessary.</returns>
    public IColorLut? Resolve(ImageDescription source, ImageDescription output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        if (_renderer.ColorTransform == ColorTransformCapability.None ||
            (_renderer.ColorTransform == ColorTransformCapability.Decomposed && !output.NeedsBakedTable))
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

    /// <summary>
    /// Disposes every cached table and takes the new renderer. Tables are imported again on the next <see cref="Resolve"/>.
    /// </summary>
    /// <param name="replacement">The old and the new renderer.</param>
    public void OnRendererReplaced(RendererReplacement replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        foreach (var lut in _luts.Values)
        {
            lut?.Dispose();
        }

        _luts.Clear();
        _renderer = replacement.New.Renderer;
    }

    /// <summary>
    /// Disposes every cached table. A second call does nothing.
    /// </summary>
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
