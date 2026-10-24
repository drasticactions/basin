using Basin.Diagnostics;

namespace Basin.Freedesktop;

/// <summary>
/// The desktop entries under one subdirectory of the XDG data directories. The first directory that supplies an id wins, and a <c>Hidden</c> entry there hides the id.
/// The scan result is cached until <see cref="Invalidate"/>. The class is not thread-safe.
/// </summary>
public sealed class DesktopEntries
{
    private static readonly BasinLogger Log = BasinLog.For("freedesktop");

    private readonly DesktopLocale _locale;
    private readonly IReadOnlyList<string>? _dataDirectories;
    private readonly string _subdirectory;
    private readonly Dictionary<string, DesktopEntry?> _byAppId = new(StringComparer.Ordinal);
    private List<DesktopEntry>? _all;
    private Dictionary<string, DesktopEntry>? _byId;
    private readonly HashSet<string> _watched = new(StringComparer.Ordinal);
    private DirectoryWatch? _watch;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopEntries"/> class. Nothing is read until the first lookup.
    /// </summary>
    /// <param name="locale">The locale for localized keys, or <see langword="null"/> for <see cref="DesktopLocale.FromEnvironment"/>.</param>
    /// <param name="dataDirectories">The data directories in order of importance, or <see langword="null"/> to ask <see cref="XdgDirectories.DataDirectories"/> on each scan.</param>
    /// <param name="subdirectory">The subdirectory to scan, for example <c>applications</c> or <c>wayland-sessions</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="subdirectory"/> is null or empty.</exception>
    public DesktopEntries(DesktopLocale? locale = null, IReadOnlyList<string>? dataDirectories = null, string subdirectory = "applications")
    {
        ArgumentException.ThrowIfNullOrEmpty(subdirectory);
        _locale = locale ?? DesktopLocale.FromEnvironment();
        _dataDirectories = dataDirectories;
        _subdirectory = subdirectory;
    }

    /// <summary>
    /// Gets the subdirectory of each data directory that this set scans.
    /// </summary>
    public string Subdirectory => _subdirectory;

    /// <summary>
    /// Occurs after a watched directory changes and the set has invalidated itself. It is raised only while <see cref="Watch(DirectoryWatch)"/> has a watch.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Makes the set invalidate itself and raise <see cref="Changed"/> when a scanned directory changes. Each existing scan root and every directory below it is watched.
    /// The set does not take ownership of <paramref name="watch"/>.
    /// </summary>
    /// <param name="watch">The watch to use, or <see langword="null"/> to stop reacting to the previous one.</param>
    public void Watch(DirectoryWatch? watch)
    {
        if (_watch is not null)
        {
            _watch.Changed -= OnWatched;
        }

        _watched.Clear();
        _watch = watch;
        if (watch is not null)
        {
            watch.Changed += OnWatched;
            WatchRoots();
        }
    }

    /// <summary>
    /// Gets every entry of the set. The first call scans the directories recursively.
    /// </summary>
    /// <returns>The cached list, which stays the same until <see cref="Invalidate"/>.</returns>
    public IReadOnlyList<DesktopEntry> All()
    {
        Scan();
        return _all!;
    }

    /// <summary>
    /// Filters <see cref="All"/> through <see cref="DesktopEntry.IsListable(IReadOnlySet{string})"/>.
    /// </summary>
    /// <param name="currentDesktop">The desktop names of the session, or <see langword="null"/> when the consumer has none.</param>
    /// <returns>A new list in the order of <see cref="All"/>.</returns>
    public IReadOnlyList<DesktopEntry> Listable(IReadOnlySet<string>? currentDesktop = null)
    {
        var listable = new List<DesktopEntry>();
        foreach (var entry in All())
        {
            if (entry.IsListable(currentDesktop))
            {
                listable.Add(entry);
            }
        }

        return listable;
    }

    /// <summary>
    /// Finds an entry by its exact desktop file id. Before the first scan it reads only the one file.
    /// A <see cref="DesktopEntry.NoDisplay"/> entry or an entry for another desktop is still returned.
    /// </summary>
    /// <param name="id">The id, with or without the <c>.desktop</c> suffix.</param>
    /// <returns>The entry, or <see langword="null"/> when no file has that id or the first file with it is hidden.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    public DesktopEntry? Find(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (!id.EndsWith(".desktop", StringComparison.Ordinal))
        {
            id += ".desktop";
        }

        if (_byId is not null)
        {
            return _byId.GetValueOrDefault(id);
        }

        foreach (var directory in _dataDirectories ?? XdgDirectories.DataDirectories())
        {
            var path = Path.Combine(directory, _subdirectory, id);
            if (File.Exists(path) && HasExactName(path, id))
            {
                return DesktopEntryReader.Parse(path, id, _locale);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the entry for a Wayland <c>app_id</c>. It tries the exact id, the id stem and <c>StartupWMClass</c> without case, then looser forms of the id.
    /// The result, a miss included, is cached per <paramref name="appId"/> until <see cref="Invalidate"/>.
    /// </summary>
    /// <param name="appId">The <c>app_id</c> to look up.</param>
    /// <returns>The first entry that matches, or <see langword="null"/> when none does or <paramref name="appId"/> is null or empty.</returns>
    public DesktopEntry? FindForAppId(string appId)
    {
        if (string.IsNullOrEmpty(appId))
        {
            return null;
        }

        if (_byAppId.TryGetValue(appId, out var memo))
        {
            return memo;
        }

        var found = Find(appId) ?? Ladder(appId);
        _byAppId[appId] = found;
        return found;
    }

    private static readonly EnumerationOptions ExactCase = new() { MatchCasing = MatchCasing.CaseSensitive };

    private static bool HasExactName(string path, string id)
    {
        foreach (var _ in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, id, ExactCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Drops the scan result and the cached <see cref="FindForAppId(string)"/> answers. The next lookup reads the directories again.
    /// </summary>
    public void Invalidate()
    {
        _all = null;
        _byId = null;
        _byAppId.Clear();
    }

    private DesktopEntry? Ladder(string appId)
    {
        var all = All();
        foreach (var entry in all)
        {
            if (Stem(entry.Id).Equals(appId, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        foreach (var entry in all)
        {
            if (string.Equals(entry.StartupWMClass, appId, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        var dot = appId.LastIndexOf('.');
        if (dot >= 0 && dot + 1 < appId.Length)
        {
            var last = appId.AsSpan(dot + 1);
            foreach (var entry in all)
            {
                if (Stem(entry.Id).Equals(last, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
        }

        var bare = Strip(appId);
        foreach (var entry in all)
        {
            if (string.Equals(Strip(Stem(entry.Id)), bare, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static ReadOnlySpan<char> Stem(string id) => id.AsSpan(0, id.Length - ".desktop".Length);

    private static string Strip(ReadOnlySpan<char> value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c != '-' && c != '_')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private void Scan()
    {
        if (_all is not null)
        {
            return;
        }

        var byId = new Dictionary<string, DesktopEntry>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in _dataDirectories ?? XdgDirectories.DataDirectories())
        {
            var root = Path.Combine(directory, _subdirectory);
            if (!Directory.Exists(root))
            {
                continue;
            }

            var files = new List<string>();
            try
            {
                files.AddRange(Directory.EnumerateFiles(root, "*.desktop", SearchOption.AllDirectories));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Log.Debug($"{root}: walk stopped early ({error.Message})");
            }

            foreach (var file in files)
            {
                var id = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '-');
                if (!taken.Add(id))
                {
                    continue;
                }

                if (DesktopEntryReader.Parse(file, id, _locale) is { } entry)
                {
                    byId[id] = entry;
                }
            }
        }

        var all = new List<DesktopEntry>(byId.Values);
        all.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        _byId = byId;
        _all = all;
        WatchRoots();
    }

    private void WatchRoots()
    {
        if (_watch is not { Available: true } watch)
        {
            return;
        }

        foreach (var directory in _dataDirectories ?? XdgDirectories.DataDirectories())
        {
            var root = Path.Combine(directory, _subdirectory);
            if (!Directory.Exists(root))
            {
                continue;
            }

            Add(watch, root);
            try
            {
                foreach (var child in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    Add(watch, child);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Log.Debug($"{root}: watch walk stopped early ({error.Message})");
            }
        }
    }

    private void Add(DirectoryWatch watch, string path)
    {
        if (!_watched.Contains(path) && watch.Add(path))
        {
            _watched.Add(path);
        }
    }

    private void OnWatched(string path)
    {
        if (!_watched.Contains(path))
        {
            return;
        }

        Invalidate();
        Changed?.Invoke();
    }
}
