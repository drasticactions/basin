namespace EightWm;

internal static class DesktopEntries
{
    public static List<DesktopEntry> Scan()
    {
        var found = new Dictionary<string, DesktopEntry>(StringComparer.Ordinal);
        foreach (var dataDir in DataDirs())
        {
            var directory = Path.Combine(dataDir, "applications");
            if (!Directory.Exists(directory))
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.desktop", SearchOption.AllDirectories);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var id = Path.GetFileName(file);
                if (found.ContainsKey(id) || Read(file, id) is not { } entry)
                {
                    continue;
                }

                found[id] = entry;
            }
        }

        var list = found.Values.ToList();
        list.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static DesktopEntry? Find(string id)
    {
        var wanted = id.EndsWith(".desktop", StringComparison.Ordinal) ? id : id + ".desktop";
        foreach (var dataDir in DataDirs())
        {
            var path = Path.Combine(dataDir, "applications", wanted);
            if (File.Exists(path) && Read(path, wanted) is { } entry)
            {
                return entry;
            }
        }

        return null;
    }

    private static DesktopEntry? Read(string path, string id)
    {
        string? name = null;
        string? exec = null;
        string? icon = null;
        var inEntry = false;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    if (inEntry)
                    {
                        break;
                    }

                    inEntry = trimmed == "[Desktop Entry]";
                    continue;
                }

                if (!inEntry || trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                if (Value(trimmed, "Name=") is { } read)
                {
                    name ??= read;
                }
                else if (Value(trimmed, "Exec=") is { } command)
                {
                    exec ??= Expand(command);
                }
                else if (Value(trimmed, "Icon=") is { } iconName)
                {
                    icon ??= iconName;
                }
                else if (Value(trimmed, "NoDisplay=") is "true" or "True")
                {
                    return null;
                }
                else if (Value(trimmed, "Hidden=") is "true" or "True")
                {
                    return null;
                }
                else if (Value(trimmed, "Type=") is { } type && type != "Application")
                {
                    return null;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return name is { Length: > 0 } && exec is { Length: > 0 }
            ? new DesktopEntry(id, name, exec, icon)
            : null;
    }

    private static string? Value(string line, string key) =>
        line.StartsWith(key, StringComparison.Ordinal) ? line[key.Length..].Trim() : null;

    private static string Expand(string exec)
    {
        var builder = new System.Text.StringBuilder(exec.Length);
        for (var i = 0; i < exec.Length; i++)
        {
            if (exec[i] == '%' && i + 1 < exec.Length)
            {
                i++;
                if (exec[i] == '%')
                {
                    builder.Append('%');
                }

                continue;
            }

            builder.Append(exec[i]);
        }

        return builder.ToString().Trim();
    }

    private static IEnumerable<string> DataDirs()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dataHome
            ? dataHome
            : Path.Combine(home, ".local", "share");

        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        var list = string.IsNullOrEmpty(dataDirs)
            ? (string[])["/usr/local/share", "/usr/share"]
            : dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in list)
        {
            yield return dir;
        }
    }
}
