namespace PlasmaHost;

internal static class KdeIni
{
    public static string? ReadEntry(string path, string group, string key)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var inGroup = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                inGroup = line == $"[{group}]";
                continue;
            }

            if (!inGroup)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator > 0 && line[..separator].Trim() == key)
            {
                return line[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    public static string ConfigPath(string name)
    {
        var home = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(home, name);
    }
}
