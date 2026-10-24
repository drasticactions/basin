namespace Basin.Freedesktop;

/// <summary>
/// Reads the icon theme name that GTK settings choose.
/// </summary>
public static class IconThemeSettings
{
    /// <summary>
    /// Reads <c>gtk-icon-theme-name</c> from <c>gtk-4.0/settings.ini</c>, then from <c>gtk-3.0/settings.ini</c>. The first non-empty value wins.
    /// </summary>
    /// <param name="configHome">The configuration directory to read, or <see langword="null"/> for <see cref="XdgDirectories.ConfigHome"/>.</param>
    /// <returns>The theme name with surrounding quotes removed, or <see langword="null"/> when no file names a theme.</returns>
    public static string? Read(string? configHome = null)
    {
        var home = configHome ?? XdgDirectories.ConfigHome;
        foreach (var version in (string[])["gtk-4.0", "gtk-3.0"])
        {
            var path = Path.Combine(home, version, "settings.ini");
            if (!File.Exists(path) || KeyFile.Read(path) is not { } groups)
            {
                continue;
            }

            foreach (var group in groups)
            {
                if (group.Name == "Settings" && group.Get("gtk-icon-theme-name") is { Length: > 0 } name)
                {
                    return name.Trim('"');
                }
            }
        }

        return null;
    }
}
