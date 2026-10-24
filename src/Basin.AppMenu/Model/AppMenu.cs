namespace Basin.AppMenu;

/// <summary>
/// A menu bar: an ordered list of titles, each with its items. It must be used on the compositor thread.
/// </summary>
public sealed class AppMenu
{
    private readonly List<AppMenuItem> _items = [];

    /// <summary>
    /// Gets the titles of the menu bar, in order. The children of each title are its items.
    /// </summary>
    public IReadOnlyList<AppMenuItem> Items => _items;

    /// <summary>
    /// Occurs after <see cref="Replace"/> or <see cref="NotifyChanged"/>. Item ids can change, so a renderer must hold items, not ids, across this event.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Finds an item by id, depth first, in the titles and all their descendants.
    /// </summary>
    /// <param name="id">The item id.</param>
    /// <returns>The first item with the id, or <see langword="null"/> when none has it.</returns>
    public AppMenuItem? Find(int id) => Find(_items, id);

    /// <summary>
    /// Replaces all titles and raises <see cref="Changed"/>.
    /// </summary>
    /// <param name="items">The new titles.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public void Replace(IEnumerable<AppMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items.Clear();
        _items.AddRange(items);
        Changed?.Invoke();
    }

    /// <summary>
    /// Raises <see cref="Changed"/> after a change to items in place.
    /// </summary>
    public void NotifyChanged() => Changed?.Invoke();

    private static AppMenuItem? Find(List<AppMenuItem> items, int id)
    {
        foreach (var item in items)
        {
            if (item.Id == id)
            {
                return item;
            }

            if (Find(item.Children, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
