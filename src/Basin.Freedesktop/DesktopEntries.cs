using Basin.Diagnostics;

namespace Basin.Freedesktop;

public sealed class DesktopEntries
{
    private static readonly BasinLogger Log = BasinLog.For("freedesktop");

    private readonly DesktopLocale _locale;
    private readonly IReadOnlyList<string>? _dataDirectories;
    private readonly Dictionary<string, DesktopEntry?> _byAppId = new(StringComparer.Ordinal);
    private List<DesktopEntry>? _all;
    private Dictionary<string, DesktopEntry>? _byId;

    public DesktopEntries(DesktopLocale? locale = null, IReadOnlyList<string>? dataDirectories = null)
    {
        _locale = locale ?? DesktopLocale.FromEnvironment();
        _dataDirectories = dataDirectories;
    }

    public IReadOnlyList<DesktopEntry> All()
    {
        Scan();
        return _all!;
    }

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
            var path = Path.Combine(directory, "applications", id);
            if (File.Exists(path) && HasExactName(path, id))
            {
                return DesktopEntryReader.Parse(path, id, _locale);
            }
        }

        return null;
    }

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
            var root = Path.Combine(directory, "applications");
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
    }
}
