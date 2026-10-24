namespace Basin.Freedesktop;

/// <summary>
/// Finds the icon file for an application id by the Icon Theme specification. It falls back to the bare theme layout and to <c>pixmaps</c>.
/// Themes and the GTK settings are read once and cached until <see cref="Invalidate"/>. The class is not thread-safe.
/// </summary>
public sealed class IconSearch
{
    /// <summary>
    /// Gets the default value of <see cref="Sizes"/>, from 512 down to 16.
    /// </summary>
    public static IReadOnlyList<int> DefaultSizes { get; } =
        [512, 256, 128, 96, 72, 64, 48, 36, 32, 24, 22, 16];

    private readonly Dictionary<string, IconTheme?> _themes = new(StringComparer.Ordinal);
    private string? _settingsTheme;
    private bool _settingsRead;

    /// <summary>
    /// Gets or sets a directory that is searched first for <c>APPID.EXT</c>, or <see langword="null"/> for none.
    /// </summary>
    public string? OverrideDirectory { get; set; }

    /// <summary>
    /// Gets or sets the file extensions to try in each directory, in order. A caller that decodes only PNG removes <c>.svg</c>.
    /// </summary>
    public IReadOnlyList<string> Extensions { get; set; } = [".svg", ".png"];

    /// <summary>
    /// Gets or sets the icon sizes in logical pixels, most wanted first. When no directory matches exactly, the one closest to the first size wins.
    /// </summary>
    public IReadOnlyList<int> Sizes { get; set; } = DefaultSizes;

    /// <summary>
    /// Gets or sets a value indicating whether the icon name comes from the <c>Icon</c> key of the desktop entry for the app id.
    /// When it is false, the app id is the icon name.
    /// </summary>
    public bool ReadDesktopEntry { get; set; } = true;

    /// <summary>
    /// Gets or sets the desktop entries that supply the icon name. When it is <see langword="null"/>, the first lookup creates a set with no locale.
    /// </summary>
    public DesktopEntries? Entries { get; set; }

    /// <summary>
    /// Gets or sets the first theme to search, or <see langword="null"/> to use the theme from <see cref="IconThemeSettings.Read"/>.
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Gets or sets the integer scale that the caller draws at. A theme directory matches exactly only at its own scale.
    /// </summary>
    public int Scale { get; set; } = 1;

    /// <summary>
    /// Gets or sets the data directories to search, or <see langword="null"/> for <see cref="XdgDirectories.DataDirectories"/>.
    /// </summary>
    public IReadOnlyList<string>? DataDirectories { get; set; }

    /// <summary>
    /// Finds the icon file for an application. An I/O or access error during the search gives <see langword="null"/>.
    /// </summary>
    /// <param name="appId">The Wayland <c>app_id</c>, or an icon name when <see cref="ReadDesktopEntry"/> is false.</param>
    /// <returns>The path of the icon file, or <see langword="null"/> when none is found.</returns>
    /// <exception cref="ArgumentException"><paramref name="appId"/> is null or empty.</exception>
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

    /// <summary>
    /// Reads the <c>Icon</c> key of the desktop entry that <see cref="DesktopEntries.FindForAppId(string)"/> finds. It creates <see cref="Entries"/> when it is <see langword="null"/>.
    /// </summary>
    /// <param name="appId">The Wayland <c>app_id</c>.</param>
    /// <returns>The icon name or path, or <see langword="null"/> when no entry is found or it has no icon.</returns>
    /// <exception cref="ArgumentException"><paramref name="appId"/> is null or empty.</exception>
    public string? DesktopEntryIcon(string appId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appId);
        return (Entries ??= new DesktopEntries(DesktopLocale.None, DataDirectories)).FindForAppId(appId)?.Icon;
    }

    /// <summary>
    /// Drops the cached themes and the cached GTK theme name, and invalidates <see cref="Entries"/>.
    /// </summary>
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
