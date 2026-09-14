namespace Basin.Freedesktop;

public sealed class IconSearch
{
    public static IReadOnlyList<int> DefaultSizes { get; } =
        [512, 256, 128, 96, 72, 64, 48, 36, 32, 24, 22, 16];

    private readonly Dictionary<string, IconTheme?> _themes = new(StringComparer.Ordinal);
    private string? _settingsTheme;
    private bool _settingsRead;

    public string? OverrideDirectory { get; set; }

    public IReadOnlyList<string> Extensions { get; set; } = [".svg", ".png"];

    public IReadOnlyList<int> Sizes { get; set; } = DefaultSizes;

    public bool ReadDesktopEntry { get; set; } = true;

    public DesktopEntries? Entries { get; set; }

    public string? Theme { get; set; }

    public int Scale { get; set; } = 1;

    public IReadOnlyList<string>? DataDirectories { get; set; }

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

    public string? DesktopEntryIcon(string appId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appId);
        return (Entries ??= new DesktopEntries(DesktopLocale.None, DataDirectories)).FindForAppId(appId)?.Icon;
    }

    public void Invalidate()
    {
        _themes.Clear();
        _settingsTheme = null;
        _settingsRead = false;
        Entries?.Invalidate();
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

        var dataDirectories = DataDirectories ?? XdgDirectories.DataDirectories();
        foreach (var theme in Chain(dataDirectories))
        {
            var found = theme.HasIndex ? LookupInTheme(theme, iconName) : LookupBareTree(theme, iconName);
            if (found is not null)
            {
                return found;
            }
        }

        foreach (var dataDir in dataDirectories)
        {
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

    private List<IconTheme> Chain(IReadOnlyList<string> dataDirectories)
    {
        if (!_settingsRead)
        {
            _settingsTheme = IconThemeSettings.Read();
            _settingsRead = true;
        }

        var chain = new List<IconTheme>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if ((Theme ?? _settingsTheme) is { Length: > 0 } first)
        {
            queue.Enqueue(first);
        }

        while (true)
        {
            if (queue.Count == 0)
            {
                if (visited.Contains("hicolor"))
                {
                    break;
                }

                queue.Enqueue("hicolor");
            }

            var name = queue.Dequeue();
            if (!visited.Add(name))
            {
                continue;
            }

            if (!_themes.TryGetValue(name, out var theme))
            {
                theme = IconTheme.Load(name, dataDirectories);
                _themes[name] = theme;
            }

            if (theme is null)
            {
                continue;
            }

            chain.Add(theme);
            foreach (var parent in theme.Inherits)
            {
                queue.Enqueue(parent);
            }
        }

        return chain;
    }

    private string? LookupInTheme(IconTheme theme, string iconName)
    {
        if (Sizes.Count == 0)
        {
            return null;
        }

        foreach (var size in Sizes)
        {
            foreach (var directory in theme.Directories)
            {
                if (directory.Matches(size, Scale) && Probe(theme, directory, iconName) is { } exact)
                {
                    return exact;
                }
            }
        }

        string? closest = null;
        var closestDistance = int.MaxValue;
        foreach (var directory in theme.Directories)
        {
            var distance = directory.Distance(Sizes[0], Scale);
            if (distance < closestDistance && Probe(theme, directory, iconName) is { } candidate)
            {
                closest = candidate;
                closestDistance = distance;
            }
        }

        return closest;
    }

    private string? Probe(IconTheme theme, IconThemeDirectory directory, string iconName)
    {
        foreach (var baseDirectory in theme.BaseDirectories)
        {
            foreach (var extension in Extensions)
            {
                var path = Path.Combine(baseDirectory, directory.Name, iconName + extension);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private string? LookupBareTree(IconTheme theme, string iconName)
    {
        foreach (var baseDirectory in theme.BaseDirectories)
        {
            if (Extensions.Contains(".svg"))
            {
                var scalable = Path.Combine(baseDirectory, "scalable", "apps", iconName + ".svg");
                if (File.Exists(scalable))
                {
                    return scalable;
                }
            }

            foreach (var size in Sizes)
            {
                var sized = Path.Combine(baseDirectory, $"{size}x{size}", "apps", iconName);
                foreach (var extension in Extensions)
                {
                    if (File.Exists(sized + extension))
                    {
                        return sized + extension;
                    }
                }
            }
        }

        return null;
    }
}
