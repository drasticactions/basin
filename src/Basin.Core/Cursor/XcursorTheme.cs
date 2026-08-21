namespace Basin;

public sealed class XcursorTheme
{
    private const uint Magic = 0x72756358;
    private const uint ImageChunkType = 0xFFFD0002;

    private readonly List<string> _directories;
    private readonly int _size;
    private readonly Dictionary<string, XcursorCursor?> _cache = new(StringComparer.Ordinal);

    private XcursorTheme(List<string> directories, int size)
    {
        _directories = directories;
        _size = size;
    }

    public static XcursorTheme? Load(string? theme, int size, IReadOnlyList<string>? searchPaths = null)
    {
        var roots = searchPaths?.ToList() ?? DefaultSearchPaths();
        var directories = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        CollectThemeDirectories(theme ?? Environment.GetEnvironmentVariable("XCURSOR_THEME") ?? "default", roots, directories, visited);
        if (!visited.Contains("default"))
        {
            CollectThemeDirectories("default", roots, directories, visited);
        }

        return directories.Count > 0 ? new XcursorTheme(directories, size) : null;
    }

    public XcursorCursor? Get(string name)
    {
        if (_cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        XcursorCursor? cursor = null;
        foreach (var directory in _directories)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                try
                {
                    cursor = Parse(File.ReadAllBytes(path), _size);
                }
                catch (Exception e) when (e is IOException or InvalidDataException)
                {
                    cursor = null;
                }

                if (cursor is not null)
                {
                    break;
                }
            }
        }

        _cache[name] = cursor;
        return cursor;
    }

    public static XcursorCursor? Parse(ReadOnlySpan<byte> data, int preferredSize)
    {
        if (data.Length < 16 || BitConverter.ToUInt32(data) != Magic)
        {
            return null;
        }

        var tocCount = BitConverter.ToUInt32(data[12..]);
        if (tocCount > 0x10000 || data.Length < 16 + tocCount * 12)
        {
            return null;
        }

        var bestSize = 0u;
        var bestDelta = long.MaxValue;
        for (var i = 0; i < tocCount; i++)
        {
            var entry = 16 + i * 12;
            if (BitConverter.ToUInt32(data[entry..]) != ImageChunkType)
            {
                continue;
            }

            var nominal = BitConverter.ToUInt32(data[(entry + 4)..]);
            var delta = Math.Abs((long)nominal - preferredSize);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestSize = nominal;
            }
        }

        if (bestSize == 0)
        {
            return null;
        }

        var frames = new List<XcursorImage>();
        for (var i = 0; i < tocCount; i++)
        {
            var entry = 16 + i * 12;
            if (BitConverter.ToUInt32(data[entry..]) != ImageChunkType ||
                BitConverter.ToUInt32(data[(entry + 4)..]) != bestSize)
            {
                continue;
            }

            var at = (int)BitConverter.ToUInt32(data[(entry + 8)..]);
            if (at + 36 > data.Length)
            {
                return null;
            }

            var width = (int)BitConverter.ToUInt32(data[(at + 16)..]);
            var height = (int)BitConverter.ToUInt32(data[(at + 20)..]);
            var hotspotX = (int)BitConverter.ToUInt32(data[(at + 24)..]);
            var hotspotY = (int)BitConverter.ToUInt32(data[(at + 28)..]);
            var delay = (int)BitConverter.ToUInt32(data[(at + 32)..]);
            if (width <= 0 || height <= 0 || width > 1024 || height > 1024 || at + 36 + width * height * 4 > data.Length)
            {
                return null;
            }

            frames.Add(new XcursorImage
            {
                Width = width,
                Height = height,
                HotspotX = hotspotX,
                HotspotY = hotspotY,
                DelayMs = delay,
                Pixels = data.Slice(at + 36, width * height * 4).ToArray(),
            });
        }

        return frames.Count > 0 ? new XcursorCursor { Frames = frames } : null;
    }

    private static List<string> DefaultSearchPaths()
    {
        var paths = new List<string>();
        var xcursorPath = Environment.GetEnvironmentVariable("XCURSOR_PATH");
        if (xcursorPath is not null)
        {
            paths.AddRange(xcursorPath.Split(':', StringSplitOptions.RemoveEmptyEntries));
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            paths.Add(Path.Combine(home, ".local/share/icons"));
            paths.Add(Path.Combine(home, ".icons"));
            paths.Add("/usr/share/icons");
            paths.Add("/usr/share/pixmaps");
        }

        return paths;
    }

    private static void CollectThemeDirectories(string theme, List<string> roots, List<string> directories, HashSet<string> visited)
    {
        if (!visited.Add(theme))
        {
            return;
        }

        foreach (var root in roots)
        {
            var themeDirectory = Path.Combine(root, theme);
            var cursors = Path.Combine(themeDirectory, "cursors");
            if (Directory.Exists(cursors))
            {
                directories.Add(cursors);
            }

            var index = Path.Combine(themeDirectory, "index.theme");
            if (File.Exists(index))
            {
                foreach (var line in File.ReadLines(index))
                {
                    if (line.StartsWith("Inherits", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line[(line.IndexOf('=') + 1)..].Trim();
                        foreach (var parent in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            CollectThemeDirectories(parent, roots, directories, visited);
                        }

                        break;
                    }
                }
            }
        }
    }
}
