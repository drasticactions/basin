using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Basin.Freedesktop;
using Basin.Icons.Skia;

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
            EightWmLog.Log.Debug(error, $"icons: cannot load {appId} at {sizePx} px");
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

        var bitmap = new WriteableBitmap(
            new PixelSize(sizePx, sizePx), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        bool drawn;
        using (var frame = bitmap.Lock())
        {
            drawn = IconRasterizer.Draw(path, frame.Address, frame.RowBytes, sizePx);
        }

        if (!drawn)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }
}
