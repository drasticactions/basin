namespace Basin;

/// <summary>
/// An Xcursor theme with the themes that it inherits, loaded at one nominal size. Cursors load from disk on first use and stay cached.
/// </summary>
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

    /// <summary>
    /// Finds a theme and the themes that it inherits. The <c>default</c> theme is always searched last.
    /// </summary>
    /// <param name="theme">The theme name. <see langword="null"/> means <c>XCURSOR_THEME</c>, or <c>default</c> when that is not set.</param>
    /// <param name="size">The nominal size in pixels to load.</param>
    /// <param name="searchPaths">The icon directories to search. <see langword="null"/> means <c>XCURSOR_PATH</c> or the standard directories.</param>
    /// <returns>The theme, or <see langword="null"/> when no theme directory has cursors.</returns>
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

    /// <summary>
    /// Loads a cursor from the first theme directory that has a file with that name. A miss is cached too.
    /// </summary>
    /// <param name="name">The cursor file name, for example <c>left_ptr</c>.</param>
    /// <returns>The cursor, or <see langword="null"/> when no directory has a valid file.</returns>
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

    /// <summary>
    /// Reads the frames of the nominal size nearest to a preferred size from Xcursor file data.
    /// </summary>
    /// <param name="data">The bytes of the Xcursor file.</param>
    /// <param name="preferredSize">The nominal size to match.</param>
    /// <returns>The cursor, or <see langword="null"/> when the data is not valid or a frame is larger than 1024 pixels.</returns>
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
