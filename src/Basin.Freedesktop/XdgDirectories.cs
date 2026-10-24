namespace Basin.Freedesktop;

/// <summary>
/// Resolves the XDG Base Directory locations. Each member reads the environment on each call.
/// </summary>
public static class XdgDirectories
{
    /// <summary>
    /// Gets <c>$XDG_DATA_HOME</c>, or <c>~/.local/share</c> when the variable is unset, empty or relative.
    /// </summary>
    public static string DataHome =>
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } home && Path.IsPathRooted(home)
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    /// <summary>
    /// Gets <c>$XDG_CONFIG_HOME</c>, or <c>~/.config</c> when the variable is unset, empty or relative.
    /// </summary>
    public static string ConfigHome =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } home && Path.IsPathRooted(home)
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

    /// <summary>
    /// Lists the data directories in order of importance.
    /// </summary>
    /// <returns><see cref="DataHome"/> first, then the absolute entries of <c>$XDG_DATA_DIRS</c>, or <c>/usr/local/share</c> and <c>/usr/share</c> when it is unset or empty.</returns>
    public static IReadOnlyList<string> DataDirectories() =>
        Directories(DataHome, "XDG_DATA_DIRS", ["/usr/local/share", "/usr/share"]);

    /// <summary>
    /// Lists the configuration directories in order of importance.
    /// </summary>
    /// <returns><see cref="ConfigHome"/> first, then the absolute entries of <c>$XDG_CONFIG_DIRS</c>, or <c>/etc/xdg</c> when it is unset or empty.</returns>
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
