using Basin.Icons.Skia;
using Basin.Render.Skia;
using SkiaSharp;

namespace Basin.WindowManager.Skia;

/// <summary>
/// Rasterizes icons by key and pixel size, through a resolver from key to file path, and caches each result, including misses.
/// </summary>
public sealed class IconRaster : IDisposable
{
    private readonly Func<string, string?> _resolve;
    private readonly Dictionary<(string Key, int SizePx), SKImage?> _cache = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="IconRaster"/> class.
    /// </summary>
    /// <param name="resolve">Maps an icon key to a file path, or to <see langword="null"/> when there is no icon.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resolve"/> is <see langword="null"/>.</exception>
    public IconRaster(Func<string, string?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        _resolve = resolve;
    }

    /// <summary>
    /// Gets the icon for a key at a size. An I/O or access error counts as a miss.
    /// </summary>
    /// <param name="key">The icon key, for example an application id.</param>
    /// <param name="sizePx">The edge length in pixels.</param>
    /// <returns>The cached image, which this object owns, or <see langword="null"/> when there is no icon.</returns>
    public SKImage? Load(string key, int sizePx)
    {
        var cacheKey = (key, sizePx);
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        SKImage? image = null;
        try
        {
            image = _resolve(key) is { } path ? IconRasterizer.Image(path, sizePx) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }

        _cache[cacheKey] = image;
        return image;
    }

    /// <summary>
    /// Releases every cached image and empties the cache.
    /// </summary>
    public void Dispose()
    {
        foreach (var image in _cache.Values)
        {
            SkiaCensus.Release(image);
        }

        _cache.Clear();
    }
}
