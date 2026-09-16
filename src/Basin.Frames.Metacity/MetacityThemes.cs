using Basin.Freedesktop;

namespace Basin.Frames.Metacity;

public static class MetacityThemes
{
    public const string SubDirectory = "metacity-1";

    public const int MajorVersion = 3;

    public const int MinorVersion = 5;

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

    public static IReadOnlyList<string> Directories(string name)
    {
        var directories = new List<string>();
        foreach (var root in SearchRoots())
        {
            directories.Add(Path.Combine(root, name, SubDirectory));
        }

        return directories;
    }

    public static string? Find(string name)
    {
        for (var major = MajorVersion; major > 0; major--)
        {
            foreach (var directory in Directories(name))
            {
                var path = Path.Combine(directory, FileName(major));
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

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

    public static string FileName(int majorVersion) => $"metacity-theme-{majorVersion}.xml";

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
