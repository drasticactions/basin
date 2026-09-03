namespace Basin.Capabilities.Defaults;

public sealed class DefaultGlobalShortcuts : IGlobalShortcuts
{
    private readonly Dictionary<(string AppId, string Id), GlobalShortcutInfo> _shortcuts = [];
    private readonly GlobalShortcutObservers _observers = new();

    public int Count => _shortcuts.Count;

    public bool TryRegister(in GlobalShortcutInfo shortcut)
    {
        if (!_shortcuts.TryAdd((shortcut.AppId, shortcut.Id), shortcut))
        {
            return false;
        }

        _observers.Registered(in shortcut);
        return true;
    }

    public void Unregister(string appId, string id)
    {
        if (_shortcuts.Remove((appId, id), out var removed))
        {
            _observers.Removed(in removed);
        }
    }

    public int Enumerate(Span<GlobalShortcutInfo> shortcuts)
    {
        if (shortcuts.Length < _shortcuts.Count)
        {
            return -1;
        }

        var i = 0;
        foreach (var shortcut in _shortcuts.Values)
        {
            shortcuts[i++] = shortcut;
        }

        return i;
    }

    public void AddObserver(IGlobalShortcutObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IGlobalShortcutObserver observer) => _observers.Remove(observer);
}
