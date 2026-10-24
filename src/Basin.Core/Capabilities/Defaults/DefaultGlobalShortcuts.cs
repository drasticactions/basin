namespace Basin.Capabilities.Defaults;

/// <summary>
/// Holds registered global shortcuts and fires them when asked. It binds no keys: the compositor calls <see cref="Trigger"/> from its own key handling.
/// </summary>
public sealed class DefaultGlobalShortcuts : IGlobalShortcuts
{
    private readonly Dictionary<(string AppId, string Id), GlobalShortcutInfo> _shortcuts = [];
    private readonly GlobalShortcutObservers _observers = new();

    /// <summary>
    /// Gets the number of registered shortcuts.
    /// </summary>
    public int Count => _shortcuts.Count;

    /// <inheritdoc/>
    public bool TryRegister(in GlobalShortcutInfo shortcut)
    {
        if (!_shortcuts.TryAdd((shortcut.AppId, shortcut.Id), shortcut))
        {
            return false;
        }

        _observers.Registered(in shortcut);
        return true;
    }

    /// <inheritdoc/>
    public void Unregister(string appId, string id)
    {
        if (_shortcuts.Remove((appId, id), out var removed))
        {
            _observers.Removed(in removed);
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public bool Trigger(string appId, string id, bool pressed, ulong timestampMs)
    {
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentNullException.ThrowIfNull(id);
        if (!_shortcuts.TryGetValue((appId, id), out var shortcut))
        {
            return false;
        }

        if (pressed)
        {
            _observers.Activated(in shortcut, timestampMs);
        }
        else
        {
            _observers.Deactivated(in shortcut, timestampMs);
        }

        return true;
    }

    /// <inheritdoc/>
    public void AddObserver(IGlobalShortcutObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IGlobalShortcutObserver observer) => _observers.Remove(observer);
}
