namespace TabWm;

internal sealed class LauncherModel
{
    private readonly List<LauncherItem> _entries = [];
    private readonly List<string> _pinned = [];

    public LauncherModel(IEnumerable<string> pinned)
    {
        _pinned.AddRange(pinned.Distinct(StringComparer.Ordinal));
    }

    public IReadOnlyList<LauncherItem> Entries => _entries;

    public IReadOnlyList<string> Pinned => _pinned;

    public event Action? Changed;

    public void Load(IEnumerable<LauncherItem> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase));
        Changed?.Invoke();
    }

    public LauncherItem? Find(string id)
    {
        foreach (var entry in _entries)
        {
            if (entry.Id == id)
            {
                return entry;
            }
        }

        return null;
    }

    public IReadOnlyList<LauncherItem> PinnedItems()
    {
        var items = new List<LauncherItem>();
        foreach (var id in _pinned)
        {
            if (Find(id) is { } item)
            {
                items.Add(item);
            }
        }

        return items;
    }

    public IReadOnlyList<(string Category, IReadOnlyList<LauncherItem> Items)> Categories()
    {
        return _entries
            .GroupBy(entry => entry.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => (group.Key, (IReadOnlyList<LauncherItem>)group.ToList()))
            .ToList();
    }

    public IReadOnlyList<LauncherItem> Search(string text)
    {
        var query = text.Trim();
        if (query.Length == 0)
        {
            return [];
        }

        var ranked = new List<(int Rank, LauncherItem Item)>();
        foreach (var entry in _entries)
        {
            if (Rank(entry, query) is { } rank)
            {
                ranked.Add((rank, entry));
            }
        }

        return ranked
            .OrderBy(pair => pair.Rank)
            .ThenBy(pair => pair.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => pair.Item)
            .ToList();
    }

    private static int? Rank(LauncherItem entry, string query)
    {
        const StringComparison Fold = StringComparison.CurrentCultureIgnoreCase;
        if (entry.Name.StartsWith(query, Fold))
        {
            return 0;
        }

        if (entry.Name.Contains(query, Fold))
        {
            return 1;
        }

        if (entry.GenericName is { } generic && generic.Contains(query, Fold))
        {
            return 2;
        }

        foreach (var keyword in entry.Keywords)
        {
            if (keyword.Contains(query, Fold))
            {
                return 3;
            }
        }

        return entry.ExecName.Contains(query, Fold) ? 4 : null;
    }

    public bool IsPinned(string id) => _pinned.Contains(id, StringComparer.Ordinal);

    public bool Pin(string id)
    {
        if (IsPinned(id))
        {
            return false;
        }

        _pinned.Add(id);
        Changed?.Invoke();
        return true;
    }

    public bool Unpin(string id)
    {
        if (!_pinned.Remove(id))
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    public bool MovePinned(string id, int index)
    {
        var from = _pinned.IndexOf(id);
        if (from < 0)
        {
            return false;
        }

        index = Math.Clamp(index, 0, _pinned.Count - 1);
        if (from == index)
        {
            return false;
        }

        _pinned.RemoveAt(from);
        _pinned.Insert(index, id);
        Changed?.Invoke();
        return true;
    }

    public void ReplacePinned(IEnumerable<string> pinned)
    {
        _pinned.Clear();
        _pinned.AddRange(pinned.Distinct(StringComparer.Ordinal));
        Changed?.Invoke();
    }
}
