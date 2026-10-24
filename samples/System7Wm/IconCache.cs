using Basin.Freedesktop;
using Basin.Icons.Skia;
using System7.Avalonia.Rendering;

namespace System7Wm;

internal sealed class IconCache
{
    private readonly Dictionary<(string, bool), System7IconFamily?> _cache = [];
    private readonly IconSearch _apps;
    private readonly IconSearch _names = new() { ReadDesktopEntry = false };

    public IconCache(DesktopEntries entries)
    {
        _apps = new IconSearch { Entries = entries };
    }

    public System7IconFamily Family(string? key, string generic, bool appId = false)
    {
        if (key is { Length: > 0 } && Load(key, appId) is { } family)
        {
            return family;
        }

        return System7IconFamily.Generic(generic) ?? System7IconFamily.Generic("document")!;
    }

    public System7IconFamily? Load(string key, bool appId)
    {
        if (_cache.TryGetValue((key, appId), out var cached))
        {
            return cached;
        }

        System7IconFamily? family = null;
        try
        {
            var path = key.StartsWith('/') ? key : (appId ? _apps : _names).Find(key);
            if (path is not null && File.Exists(path) && IconRasterizer.Argb(path, 32) is { } large && IconRasterizer.Argb(path, 16) is { } small)
            {
                family = System7IconFamily.FromArgb(large, small);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            System7Log.Log.Debug(error, $"icons: cannot load {key}");
        }

        _cache[(key, appId)] = family;
        return family;
    }

    public void Clear()
    {
        _cache.Clear();
        _apps.Invalidate();
        _names.Invalidate();
    }
}
