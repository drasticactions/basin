using System.Collections;

namespace Basin.Capabilities;

public sealed class WorkspaceSet<T> : IReadOnlyList<T>
    where T : class
{
    private readonly List<T> _items = [];
    private ulong _nextId;

    public Func<T, WorkspaceInfo>? Describe { get; set; }

    public Func<T, ulong>? IdOf { get; set; }

    public T? Active { get; set; }

    public int ActiveIndex => Active is null ? -1 : _items.IndexOf(Active);

    public int Count => _items.Count;

    public T this[int index] => _items[index];

    public ulong NextId() => ++_nextId;

    public void Add(T workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _items.Add(workspace);
        Active ??= workspace;
    }

    public void Insert(int index, T workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _items.Insert(index, workspace);
        Active ??= workspace;
    }

    public bool Remove(T workspace)
    {
        var index = _items.IndexOf(workspace);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index)
    {
        var wasActive = ReferenceEquals(_items[index], Active);
        _items.RemoveAt(index);
        if (!wasActive)
        {
            return;
        }

        Active = _items.Count == 0 ? null : _items[Math.Min(index, _items.Count - 1)];
    }

    public void Clear()
    {
        _items.Clear();
        Active = null;
    }

    public bool Contains(T workspace) => _items.Contains(workspace);

    public int IndexOf(T workspace) => _items.IndexOf(workspace);

    public T? ById(ulong id)
    {
        if (IdOf is not { } idOf)
        {
            return null;
        }

        foreach (var item in _items)
        {
            if (idOf(item) == id)
            {
                return item;
            }
        }

        return null;
    }

    public int Fill(Span<WorkspaceInfo> workspaces)
    {
        if (Describe is not { } describe)
        {
            return 0;
        }

        if (_items.Count > workspaces.Length)
        {
            return -1;
        }

        for (var i = 0; i < _items.Count; i++)
        {
            workspaces[i] = describe(_items[i]);
        }

        return _items.Count;
    }

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
