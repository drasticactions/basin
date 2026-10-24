namespace Basin.Shell.Nested;

/// <summary>
/// A most-recently-used list of focused items, for focus fallback and window switching.
/// </summary>
/// <typeparam name="T">The item type, compared by its <see cref="object.Equals(object)"/>.</typeparam>
public sealed class FocusHistory<T> where T : class
{
    private readonly List<T> _items = [];

    /// <summary>
    /// Gets the items, the most recently touched first. The list is live and changes with each call.
    /// </summary>
    public IReadOnlyList<T> Items => _items;

    /// <summary>
    /// Gets the most recently touched item, or <see langword="null"/> when the history is empty.
    /// </summary>
    public T? MostRecent => _items.Count > 0 ? _items[0] : null;

    /// <summary>
    /// Moves an item to the front of the history. It adds the item when the history does not contain it.
    /// </summary>
    /// <param name="item">The item that got focus.</param>
    public void Touch(T item)
    {
        _items.Remove(item);
        _items.Insert(0, item);
    }

    /// <summary>
    /// Removes an item from the history. It does nothing when the history does not contain it.
    /// </summary>
    /// <param name="item">The item to remove.</param>
    public void Remove(T item) => _items.Remove(item);

    /// <summary>
    /// Gets the item after <paramref name="current"/>, and wraps to the first item at the end.
    /// </summary>
    /// <param name="current">The item to step from, or <see langword="null"/>.</param>
    /// <returns>The next item, the first item when <paramref name="current"/> is not in the history, or <see langword="null"/> when the history is empty.</returns>
    public T? Next(T? current) => Step(current, 1);

    /// <summary>
    /// Gets the item before <paramref name="current"/>, and wraps to the last item at the start.
    /// </summary>
    /// <param name="current">The item to step from, or <see langword="null"/>.</param>
    /// <returns>The previous item, the last item when <paramref name="current"/> is not in the history, or <see langword="null"/> when the history is empty.</returns>
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
