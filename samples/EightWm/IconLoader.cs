using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Basin.Freedesktop;
using Basin.Render.Skia;
using SkiaSharp;
using Svg.Skia;

namespace EightWm;

internal sealed class IconLoader : IDisposable
{
    private readonly Dictionary<(string AppId, int SizePx), WriteableBitmap?> _cache = [];

    public IImage? Load(string appId, int sizePx)
    {
        if (sizePx <= 0)
        {
            return null;
        }

        var key = (appId, sizePx);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        WriteableBitmap? image = null;
        try
        {
            image = LoadUncached(appId, sizePx);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }

        _cache[key] = image;
        return image;
    }

    public void Clear()
    {
        foreach (var image in _cache.Values)
        {
            image?.Dispose();
        }

        _cache.Clear();
    }

    public void Dispose() => Clear();

    private static WriteableBitmap? LoadUncached(string appId, int sizePx)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var search = new IconSearch
        {
            OverrideDirectory = Path.Combine(home, ".config", "eight-wm", "icons"),
        };

        if (search.Find(appId) is not { } path)
        {
            return null;
        }

        var raster = SkiaCensus.Track(new SKBitmap(new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul)));
        try
        {
            return Rasterize(path, raster) ? Copy(raster) : null;
        }
        finally
        {
            SkiaCensus.Release(raster);
        }
    }

    private static bool Rasterize(string path, SKBitmap target)
    {
        var sizePx = target.Width;
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            using var svg = new SKSvg();
            if (svg.Load(path) is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
            {
                return false;
            }

            var bounds = picture.CullRect;
            var scale = Math.Min(sizePx / bounds.Width, sizePx / bounds.Height);
            canvas.Translate(
                (sizePx - (bounds.Width * scale)) / 2f,
                (sizePx - (bounds.Height * scale)) / 2f);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();
            return true;
        }

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return false;
        }

        var fit = Math.Min(sizePx / (float)bitmap.Width, sizePx / (float)bitmap.Height);
        var width = bitmap.Width * fit;
        var height = bitmap.Height * fit;
        using var image = SKImage.FromBitmap(bitmap);
        using var paint = new SKPaint();
        canvas.DrawImage(
            image,
            new SKRect((sizePx - width) / 2f, (sizePx - height) / 2f, (sizePx + width) / 2f, (sizePx + height) / 2f),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            paint);
        canvas.Flush();
        return true;
    }

    private static unsafe WriteableBitmap Copy(SKBitmap source)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(source.Width, source.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var frame = bitmap.Lock();
        var rowBytes = Math.Min(frame.RowBytes, source.RowBytes);
        var from = (byte*)source.GetPixels();
        var to = (byte*)frame.Address;
        for (var row = 0; row < source.Height; row++)
        {
            Buffer.MemoryCopy(from + (row * source.RowBytes), to + (row * frame.RowBytes), frame.RowBytes, rowBytes);
        }

        return bitmap;
    }
}
