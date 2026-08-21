using SkiaSharp;
using Svg.Skia;

namespace RetroWm;

internal sealed class IconLoader
{
    private static readonly int[] Sizes = [512, 256, 128, 96, 72, 64, 48, 36, 32, 24, 22, 16];

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

        _cache[key] = image;
        return image;
    }

    private static SKImage? LoadUncached(string appId, int sizePx)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var extension in (string[])[".svg", ".png"])
        {
            var overridePath = Path.Combine(home, ".config", "retro-wm", "icons", appId + extension);
            if (File.Exists(overridePath) && Rasterize(overridePath, sizePx) is { } fromOverride)
            {
                return fromOverride;
            }
        }

        var iconName = DesktopEntryIcon(appId);
        if (iconName is null)
        {
            return null;
        }

        if (Path.IsPathRooted(iconName))
        {
            return File.Exists(iconName) ? Rasterize(iconName, sizePx) : null;
        }

        foreach (var dataDir in DataDirs())
        {
            var scalable = Path.Combine(dataDir, "icons", "hicolor", "scalable", "apps", iconName + ".svg");
            if (File.Exists(scalable) && Rasterize(scalable, sizePx) is { } fromScalable)
            {
                return fromScalable;
            }

            foreach (var size in Sizes)
            {
                var sized = Path.Combine(dataDir, "icons", "hicolor", $"{size}x{size}", "apps", iconName);
                foreach (var extension in (string[])[".png", ".svg"])
                {
                    if (File.Exists(sized + extension) && Rasterize(sized + extension, sizePx) is { } fromSized)
                    {
                        return fromSized;
                    }
                }
            }

            foreach (var extension in (string[])[".png", ".svg"])
            {
                var pixmap = Path.Combine(dataDir, "pixmaps", iconName + extension);
                if (File.Exists(pixmap) && Rasterize(pixmap, sizePx) is { } fromPixmaps)
                {
                    return fromPixmaps;
                }
            }
        }

        return null;
    }

    private static string? DesktopEntryIcon(string appId)
    {
        foreach (var dataDir in DataDirs())
        {
            var path = Path.Combine(dataDir, "applications", appId + ".desktop");
            if (!File.Exists(path))
            {
                continue;
            }

            var inEntry = false;
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    inEntry = trimmed == "[Desktop Entry]";
                    continue;
                }

                if (inEntry && trimmed.StartsWith("Icon=", StringComparison.Ordinal))
                {
                    var value = trimmed["Icon=".Length..].Trim();
                    return value.Length > 0 ? value : null;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> DataDirs()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            is { Length: > 0 } dataHome ? dataHome : Path.Combine(home, ".local", "share");

        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        var list = string.IsNullOrEmpty(dataDirs)
            ? (string[])["/usr/local/share", "/usr/share"]
            : dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in list)
        {
            yield return dir;
        }
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
