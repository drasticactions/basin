namespace Basin.Cli;

public sealed class IconSearch
{
    public static IReadOnlyList<int> DefaultSizes { get; } =
        [512, 256, 128, 96, 72, 64, 48, 36, 32, 24, 22, 16];

    public string? OverrideDirectory { get; set; }

    public IReadOnlyList<string> Extensions { get; set; } = [".svg", ".png"];

    public IReadOnlyList<int> Sizes { get; set; } = DefaultSizes;

    public bool ReadDesktopEntry { get; set; } = true;

    public string? Find(string appId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appId);
        try
        {
            return FindCore(appId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IEnumerable<string> DataDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            is { Length: > 0 } dataHome ? dataHome : Path.Combine(home, ".local", "share");

        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        var list = string.IsNullOrEmpty(dataDirs)
            ? (string[])["/usr/local/share", "/usr/share"]
            : dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in list)
        {
            yield return dir;
        }
    }

    public static string? DesktopEntryIcon(string appId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appId);
        foreach (var dataDir in DataDirectories())
        {
            var path = Path.Combine(dataDir, "applications", appId + ".desktop");
            if (!File.Exists(path))
            {
                continue;
            }

            var inEntry = false;
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    inEntry = trimmed == "[Desktop Entry]";
                    continue;
                }

                if (inEntry && trimmed.StartsWith("Icon=", StringComparison.Ordinal))
                {
                    var value = trimmed["Icon=".Length..].Trim();
                    return value.Length > 0 ? value : null;
                }
            }
        }

        return null;
    }

    private string? FindCore(string appId)
    {
        if (OverrideDirectory is { Length: > 0 } directory)
        {
            foreach (var extension in Extensions)
            {
                var path = Path.Combine(directory, appId + extension);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        var iconName = ReadDesktopEntry ? DesktopEntryIcon(appId) ?? appId : appId;
        if (Path.IsPathRooted(iconName))
        {
            return File.Exists(iconName) ? iconName : null;
        }

        foreach (var dataDir in DataDirectories())
        {
            if (Extensions.Contains(".svg"))
            {
                var scalable = Path.Combine(dataDir, "icons", "hicolor", "scalable", "apps", iconName + ".svg");
                if (File.Exists(scalable))
                {
                    return scalable;
                }
            }

            foreach (var size in Sizes)
            {
                var sized = Path.Combine(dataDir, "icons", "hicolor", $"{size}x{size}", "apps", iconName);
                foreach (var extension in Extensions)
                {
                    if (File.Exists(sized + extension))
                    {
                        return sized + extension;
                    }
                }
            }

            foreach (var extension in Extensions)
            {
                var pixmap = Path.Combine(dataDir, "pixmaps", iconName + extension);
                if (File.Exists(pixmap))
                {
                    return pixmap;
                }
            }
        }

        return null;
    }
}
