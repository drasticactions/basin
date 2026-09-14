namespace Basin.Freedesktop;

public static class IconThemeSettings
{
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
