namespace Basin.Shell.Nested;

public sealed class FocusHistory<T> where T : class
{
    private readonly List<T> _items = [];

    public IReadOnlyList<T> Items => _items;

    public T? MostRecent => _items.Count > 0 ? _items[0] : null;

    public void Touch(T item)
    {
        _items.Remove(item);
        _items.Insert(0, item);
    }

    public void Remove(T item) => _items.Remove(item);

    public T? Next(T? current) => Step(current, 1);

    public T? Previous(T? current) => Step(current, -1);

    private T? Step(T? current, int delta)
    {
        if (_items.Count == 0)
            return null;
        var index = current is null ? -1 : _items.IndexOf(current);
        if (index < 0)
            return delta > 0 ? _items[0] : _items[^1];
        return _items[(index + delta + _items.Count) % _items.Count];
    }
}
