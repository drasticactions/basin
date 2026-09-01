using Basin.Cli;

namespace DeskbarWm;

internal static class DesktopEntries
{
    private static readonly Dictionary<string, string?> NameCache = [];
    private static List<AppEntry>? _all;

    public static string? NameFor(string appId)
    {
        if (NameCache.TryGetValue(appId, out var cached))
        {
            return cached;
        }

        string? name = null;
        try
        {
            name = Parse(FindPath(appId))?.Name;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }

        NameCache[appId] = name;
        return name;
    }

    public static AppEntry? EntryFor(string appId)
    {
        try
        {
            return Parse(FindPath(appId));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<AppEntry> All()
    {
        if (_all is not null)
        {
            return _all;
        }

        var byId = new Dictionary<string, AppEntry>();
        foreach (var dataDir in IconSearch.DataDirectories())
        {
            var directory = Path.Combine(dataDir, "applications");
            if (!Directory.Exists(directory))
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.desktop");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (byId.ContainsKey(id))
                {
                    continue;
                }

                try
                {
                    if (Parse(file) is { } entry)
                    {
                        byId[id] = entry;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        var all = new List<AppEntry>(byId.Values);
        all.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        _all = all;
        return all;
    }

    public static void InvalidateAll() => _all = null;

    public static string[] SplitExec(string exec)
    {
        var parts = new List<string>();
        var current = string.Empty;
        var quoted = false;
        for (var i = 0; i < exec.Length; i++)
        {
            var c = exec[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ' ' && !quoted)
            {
                if (current.Length > 0)
                {
                    parts.Add(current);
                    current = string.Empty;
                }
            }
            else if (c == '%' && i + 1 < exec.Length)
            {
                i++;
            }
            else
            {
                current += c;
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current);
        }

        return [.. parts];
    }

    private static string? FindPath(string appId)
    {
        foreach (var dataDir in IconSearch.DataDirectories())
        {
            var path = Path.Combine(dataDir, "applications", appId + ".desktop");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static AppEntry? Parse(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        string? name = null;
        string? exec = null;
        var skip = false;
        var inEntry = false;
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length > 0 && line[0] == '[')
            {
                if (inEntry)
                {
                    break;
                }

                inEntry = line.StartsWith("[Desktop Entry]", StringComparison.Ordinal);
                continue;
            }

            if (!inEntry)
            {
                continue;
            }

            if (line.StartsWith("Name=", StringComparison.Ordinal))
            {
                name ??= line[5..];
            }
            else if (line.StartsWith("Exec=", StringComparison.Ordinal))
            {
                exec ??= line[5..];
            }
            else if (line is "NoDisplay=true" or "Hidden=true" or "Terminal=true")
            {
                skip = true;
            }
        }

        if (skip || name is null || exec is null)
        {
            return null;
        }

        return new AppEntry(Path.GetFileNameWithoutExtension(path), name, exec);
    }
}
