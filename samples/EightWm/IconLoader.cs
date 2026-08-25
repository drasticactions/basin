using Basin.Cli;
using Basin.Render.Skia;
using SkiaSharp;
using Svg.Skia;

namespace EightWm;

internal sealed class IconLoader : IDisposable
{
    private readonly Dictionary<(string AppId, int SizePx), SKImage?> _cache = [];

    public SKImage? Load(string appId, int sizePx)
    {
        var key = (appId, sizePx);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        SKImage? image = null;
        try
        {
            image = LoadUncached(appId, sizePx);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }

        if (image is not null)
        {
            SkiaCensus.Track(image);
        }

        _cache[key] = image;
        return image;
    }

    public void Dispose()
    {
        foreach (var image in _cache.Values)
        {
            SkiaCensus.Release(image);
        }

        _cache.Clear();
    }

    private static SKImage? LoadUncached(string appId, int sizePx)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var search = new IconSearch
        {
            OverrideDirectory = Path.Combine(home, ".config", "eight-wm", "icons"),
        };

        return search.Find(appId) is { } path ? Rasterize(path, sizePx) : null;
    }

    private static SKImage? Rasterize(string path, int sizePx)
    {
        var info = new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            using var svg = new SKSvg();
            if (svg.Load(path) is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
            {
                return null;
            }

            using var surface = SKSurface.Create(info);
            if (surface is null)
            {
                return null;
            }

            var bounds = picture.CullRect;
            var scale = Math.Min(sizePx / bounds.Width, sizePx / bounds.Height);
            surface.Canvas.Translate(
                (sizePx - (bounds.Width * scale)) / 2f,
                (sizePx - (bounds.Height * scale)) / 2f);
            surface.Canvas.Scale(scale);
            surface.Canvas.Translate(-bounds.Left, -bounds.Top);
            surface.Canvas.DrawPicture(picture);
            surface.Canvas.Flush();
            return surface.Snapshot();
        }

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return null;
        }

        using var rasterSurface = SKSurface.Create(info);
        if (rasterSurface is null)
        {
            return null;
        }

        var fit = Math.Min(sizePx / (float)bitmap.Width, sizePx / (float)bitmap.Height);
        var width = bitmap.Width * fit;
        var height = bitmap.Height * fit;
        using var image = SKImage.FromBitmap(bitmap);
        using var paint = new SKPaint();
        rasterSurface.Canvas.DrawImage(
            image,
            new SKRect((sizePx - width) / 2f, (sizePx - height) / 2f, (sizePx + width) / 2f, (sizePx + height) / 2f),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            paint);
        rasterSurface.Canvas.Flush();
        return rasterSurface.Snapshot();
    }
}
