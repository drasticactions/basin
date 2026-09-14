namespace Basin.Freedesktop;

public static class XdgDirectories
{
    public static string DataHome =>
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } home && Path.IsPathRooted(home)
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    public static string ConfigHome =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } home && Path.IsPathRooted(home)
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

    public static IReadOnlyList<string> DataDirectories() =>
        Directories(DataHome, "XDG_DATA_DIRS", ["/usr/local/share", "/usr/share"]);

    public static IReadOnlyList<string> ConfigDirectories() =>
        Directories(ConfigHome, "XDG_CONFIG_DIRS", ["/etc/xdg"]);

    private static List<string> Directories(string home, string variable, string[] defaults)
    {
        var list = new List<string> { home };
        var configured = Environment.GetEnvironmentVariable(variable);
        var entries = string.IsNullOrEmpty(configured) ? defaults : configured.Split(':');
        foreach (var entry in entries)
        {
            if (entry.Length > 0 && Path.IsPathRooted(entry))
            {
                list.Add(entry);
            }
        }

        return list;
    }
}
