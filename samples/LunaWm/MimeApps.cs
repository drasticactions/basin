namespace LunaWm;

internal static class MimeApps
{
    public static string? DefaultFor(string mimeType)
    {
        foreach (var file in Files())
        {
            if (Read(file, mimeType) is { } id)
            {
                return id;
            }
        }

        return null;
    }

    private static IEnumerable<string> Files()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".config");
        yield return Path.Combine(config, "mimeapps.list");
        foreach (var dir in (Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS") is { Length: > 0 } dirs ? dirs : "/etc/xdg").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(dir, "mimeapps.list");
        }

        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : Path.Combine(home, ".local", "share");
        yield return Path.Combine(data, "applications", "mimeapps.list");
        foreach (var dir in (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(dir, "applications", "mimeapps.list");
        }
    }

    private static string? Read(string file, string mimeType)
    {
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            var inDefaults = false;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inDefaults = line == "[Default Applications]";
                    continue;
                }

                if (!inDefaults)
                {
                    continue;
                }

                var equals = line.IndexOf('=');
                if (equals > 0 && line[..equals].Trim() == mimeType)
                {
                    var first = line[(equals + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return first.Length > 0 ? first[0] : null;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Basin.Diagnostics.BasinLog.For("LunaWm").Debug(error, $"mime: cannot read {file}");
        }

        return null;
    }
}
