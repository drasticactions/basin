namespace Basin.Shell.River;

internal sealed class RenderList<T>
    where T : class
{
    private readonly List<T> _entries = [];

    public List<T> Entries => _entries;

    public int Count => _entries.Count;

    public void Add(T entry)
    {
        if (!_entries.Contains(entry))
        {
            _entries.Add(entry);
        }
    }

    public bool Remove(T entry) => _entries.Remove(entry);

    public void Clear() => _entries.Clear();

    public bool Contains(T entry) => _entries.Contains(entry);

    public int IndexOf(T entry) => _entries.IndexOf(entry);

    public void PlaceTop(T entry)
    {
        if (_entries.Remove(entry))
        {
            _entries.Add(entry);
        }
    }

    public void PlaceBottom(T entry)
    {
        if (_entries.Remove(entry))
        {
            _entries.Insert(0, entry);
        }
    }

    public void PlaceAbove(T entry, T other)
    {
        if (ReferenceEquals(entry, other) || !_entries.Contains(other) || !_entries.Remove(entry))
        {
            return;
        }

        _entries.Insert(_entries.IndexOf(other) + 1, entry);
    }

    public void PlaceBelow(T entry, T other)
    {
        if (ReferenceEquals(entry, other) || !_entries.Contains(other) || !_entries.Remove(entry))
        {
            return;
        }

        _entries.Insert(_entries.IndexOf(other), entry);
    }
}
