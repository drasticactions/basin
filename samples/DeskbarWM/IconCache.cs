using Basin.WindowManager.Skia;
using SkiaSharp;

namespace DeskbarWm;

internal sealed class IconCache(IconRaster fallback)
{
    private readonly Dictionary<(string AppId, int SizePx), SKImage?> _cache = [];
    private string _haikuDirectory = string.Empty;

    public void Reconfigure(Config config)
    {
        if (_haikuDirectory != config.HaikuIconDirectory)
        {
            _haikuDirectory = config.HaikuIconDirectory;
            _cache.Clear();
        }
    }

    public SKImage? Load(string appId, int sizePx)
    {
        var key = (appId, sizePx);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = TryHvif(appId, sizePx) ?? fallback.Load(appId, sizePx);
        _cache[key] = image;
        return image;
    }

    private SKImage? TryHvif(string appId, int sizePx)
    {
        if (_haikuDirectory.Length == 0 || !Directory.Exists(_haikuDirectory))
        {
            return null;
        }

        foreach (var candidate in Candidates(appId))
        {
            var path = Path.Combine(_haikuDirectory, candidate + ".hvif");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var data = File.ReadAllBytes(path);
                if (HvifReader.Parse(data) is not { } icon)
                {
                    return null;
                }

                var info = new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                if (surface is null)
                {
                    return null;
                }

                surface.Canvas.Clear(SKColors.Transparent);
                icon.Render(surface.Canvas, sizePx);
                surface.Canvas.Flush();
                return surface.Snapshot();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string appId)
    {
        yield return appId;
        var lower = appId.ToLowerInvariant();
        if (lower != appId)
        {
            yield return lower;
        }

        var dot = appId.LastIndexOf('.');
        if (dot >= 0 && dot + 1 < appId.Length)
        {
            yield return appId[(dot + 1)..].ToLowerInvariant();
        }
    }
}
