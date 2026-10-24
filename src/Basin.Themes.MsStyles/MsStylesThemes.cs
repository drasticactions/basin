using Basin.Diagnostics;
using Basin.Freedesktop;

namespace Basin.Themes.MsStyles;

/// <summary>
/// Finds installed visual styles. A theme is a directory that holds an <c>.msstyles</c> file. Names match without case.
/// </summary>
public static class MsStylesThemes
{
    private static readonly BasinLogger Log = BasinLog.For("msstyles");

    /// <summary>
    /// The file name extension of a visual style.
    /// </summary>
    public const string Extension = ".msstyles";

    /// <summary>
    /// Lists the directories that hold GTK-style theme directories: <c>~/.themes</c>, then <c>themes</c> under each XDG data directory.
    /// </summary>
    /// <returns>The directory paths in search order. A path is listed whether it exists or not.</returns>
    public static IReadOnlyList<string> ThemeRoots()
    {
        var roots = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            roots.Add(System.IO.Path.Combine(home, ".themes"));
        }

        AddDataRoots(roots, "themes");
        return roots;
    }

    /// <summary>
    /// Lists the <c>msstyles</c> directories under each XDG data directory.
    /// </summary>
    /// <returns>The directory paths in search order. A path is listed whether it exists or not.</returns>
    public static IReadOnlyList<string> StyleRoots()
    {
        var roots = new List<string>();
        AddDataRoots(roots, "msstyles");
        return roots;
    }

    /// <summary>
    /// Lists the theme directory of the wine prefix. The prefix is <c>WINEPREFIX</c>, or <c>~/.wine</c> when it is not set.
    /// </summary>
    /// <returns>The <c>drive_c/windows/resources/themes</c> directory of the prefix, or an empty list when it does not exist.</returns>
    public static IReadOnlyList<string> WineRoots()
    {
        var roots = new List<string>();
        var prefix = Environment.GetEnvironmentVariable("WINEPREFIX");
        if (string.IsNullOrEmpty(prefix))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            prefix = string.IsNullOrEmpty(home) ? null : System.IO.Path.Combine(home, ".wine");
        }

        if (prefix is not null && Descend(prefix, "drive_c", "windows", "resources", "themes") is { } themes)
        {
            roots.Add(themes);
        }

        return roots;
    }

    /// <summary>
    /// Finds the file of an installed theme. A name that contains a slash is a path.
    /// </summary>
    /// <param name="name">The theme name, which is the name of its directory, or a path.</param>
    /// <returns>The path of the <c>.msstyles</c> file, or <see langword="null"/> when no file is found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static string? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Contains('/', StringComparison.Ordinal))
        {
            return File.Exists(name) ? name : null;
        }

        foreach (var root in ThemeRoots())
        {
            if (Child(root, name, directory: true) is { } directory && InThemeDirectory(directory, name) is { } path)
            {
                return path;
            }
        }

        foreach (var root in StyleRoots().Concat(WineRoots()))
        {
            if (Child(root, name, directory: true) is { } directory && Child(directory, name + Extension, directory: false) is { } path)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Lists every installed theme. It reads each file's resource directory and its color and size names, but parses no class.
    /// A file that is not a format 3 or 4 visual style is not supported, and the list does not include it.
    /// </summary>
    /// <returns>The themes, sorted by name. When two roots have the same name, the first root wins.</returns>
    public static IReadOnlyList<MsStylesThemeEntry> Available()
    {
        var found = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ThemeRoots())
        {
            foreach (var directory in Directories(root))
            {
                var name = System.IO.Path.GetFileName(directory);
                if (!found.ContainsKey(name) && InThemeDirectory(directory, name) is { } path)
                {
                    found[name] = path;
                }
            }
        }

        foreach (var root in StyleRoots().Concat(WineRoots()))
        {
            foreach (var directory in Directories(root))
            {
                var name = System.IO.Path.GetFileName(directory);
                if (!found.ContainsKey(name) && Child(directory, name + Extension, directory: false) is { } path)
                {
                    found[name] = path;
                }
            }
        }

        var entries = new List<MsStylesThemeEntry>(found.Count);
        foreach (var (name, path) in found)
        {
            try
            {
                var (colors, sizes) = MsStylesTheme.ReadNames(path);
                entries.Add(new MsStylesThemeEntry(name, path, colors, sizes));
            }
            catch (Exception error) when (error is MsStylesFormatException or IOException or UnauthorizedAccessException)
            {
                Log.Debug(error, $"themes: {path} skipped");
            }
        }

        return entries;
    }

    private static void AddDataRoots(List<string> roots, string subdirectory)
    {
        var home = System.IO.Path.Combine(XdgDirectories.DataHome, subdirectory);
        if (!roots.Contains(home))
        {
            roots.Add(home);
        }

        foreach (var dataDirectory in XdgDirectories.DataDirectories())
        {
            var candidate = System.IO.Path.Combine(dataDirectory, subdirectory);
            if (!roots.Contains(candidate))
            {
                roots.Add(candidate);
            }
        }
    }

    private static string? InThemeDirectory(string directory, string name)
    {
        if (Child(directory, name + Extension, directory: false) is { } named)
        {
            return named;
        }

        string? single = null;
        foreach (var file in Files(directory))
        {
            if (!file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (single is not null)
            {
                return null;
            }

            single = file;
        }

        return single;
    }

    private static string? Descend(string root, params string[] names)
    {
        var current = root;
        foreach (var name in names)
        {
            if (Child(current, name, directory: true) is not { } next)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static string? Child(string parent, string name, bool directory = false)
    {
        var exact = System.IO.Path.Combine(parent, name);
        if (directory ? Directory.Exists(exact) : File.Exists(exact))
        {
            return exact;
        }

        foreach (var entry in directory ? Directories(parent) : Files(parent))
        {
            if (string.Equals(System.IO.Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static IEnumerable<string> Directories(string parent)
    {
        try
        {
            return Directory.Exists(parent) ? Directory.GetDirectories(parent) : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> Files(string parent)
    {
        try
        {
            return Directory.Exists(parent) ? Directory.GetFiles(parent) : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
