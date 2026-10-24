using Basin.Freedesktop;

namespace Basin.Frames.Metacity;

/// <summary>
/// The theme discovery ladder of marco: the search folders, the file names and the list of installed themes.
/// </summary>
public static class MetacityThemes
{
    /// <summary>
    /// The folder inside a theme that holds its metacity files.
    /// </summary>
    public const string SubDirectory = "metacity-1";

    /// <summary>
    /// The highest major format version that the engine reads.
    /// </summary>
    public const int MajorVersion = 3;

    /// <summary>
    /// The highest minor format version that the engine reads.
    /// </summary>
    public const int MinorVersion = 5;

    /// <summary>
    /// Lists the theme roots in search order: <c>~/.themes</c>, <c>$XDG_DATA_HOME/themes</c>, then <c>themes</c> in each <c>$XDG_DATA_DIRS</c> entry, with no repeats.
    /// </summary>
    /// <returns>The root folders. They are not checked for existence.</returns>
    public static IReadOnlyList<string> SearchRoots()
    {
        var roots = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            roots.Add(Path.Combine(home, ".themes"));
        }

        roots.Add(Path.Combine(XdgDirectories.DataHome, "themes"));
        foreach (var dataDirectory in XdgDirectories.DataDirectories())
        {
            var candidate = Path.Combine(dataDirectory, "themes");
            if (!roots.Contains(candidate))
            {
                roots.Add(candidate);
            }
        }

        return roots;
    }

    /// <summary>
    /// Lists the <c>metacity-1</c> folders where a theme can be, in search order.
    /// </summary>
    /// <param name="name">The theme name.</param>
    /// <returns>One folder for each search root. They are not checked for existence.</returns>
    public static IReadOnlyList<string> Directories(string name)
    {
        var directories = new List<string>();
        foreach (var root in SearchRoots())
        {
            directories.Add(Path.Combine(root, name, SubDirectory));
        }

        return directories;
    }

    /// <summary>
    /// Finds the file that <see cref="MetacityTheme.Load"/> loads: the first one that exists, highest format first, whose root <c>version</c> the engine satisfies.
    /// A file that does not parse counts as satisfied, because the load stops on it with the parse error.
    /// </summary>
    /// <param name="name">The theme name.</param>
    /// <returns>The file path, or <see langword="null"/> when no file exists or every file needs a newer engine.</returns>
    public static string? Find(string name)
    {
        for (var major = MajorVersion; major > 0; major--)
        {
            foreach (var directory in Directories(name))
            {
                var path = Path.Combine(directory, FileName(major));
                if (File.Exists(path) && MetacityThemeReader.RootVersionSatisfied(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Lists the installed themes that have a <c>metacity-theme-*.xml</c> file in a <c>metacity-1</c> folder.
    /// </summary>
    /// <returns>The theme names, sorted and with no repeats.</returns>
    public static IReadOnlyList<string> Available()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var root in SearchRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var themeDirectory in Directory.EnumerateDirectories(root))
            {
                var metacity = Path.Combine(themeDirectory, SubDirectory);
                if (Directory.Exists(metacity) && Directory.EnumerateFiles(metacity, "metacity-theme-*.xml").Any())
                {
                    names.Add(Path.GetFileName(themeDirectory));
                }
            }
        }

        return names.ToList();
    }

    /// <summary>
    /// Gives the theme file name for a major format version.
    /// </summary>
    /// <param name="majorVersion">The major version.</param>
    /// <returns>The name <c>metacity-theme-N.xml</c>.</returns>
    public static string FileName(int majorVersion) => $"metacity-theme-{majorVersion}.xml";

    /// <summary>
    /// Reads the major format version from a theme file name.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The number in <c>metacity-theme-N.xml</c>, or 1 when the name has another form.</returns>
    public static int MajorVersionOf(string path)
    {
        var file = Path.GetFileName(path);
        if (file.StartsWith("metacity-theme-", StringComparison.Ordinal) && file.EndsWith(".xml", StringComparison.Ordinal) &&
            int.TryParse(file.AsSpan("metacity-theme-".Length, file.Length - "metacity-theme-".Length - ".xml".Length), out var major))
        {
            return major;
        }

        return 1;
    }
}
