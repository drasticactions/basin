namespace Basin.Capabilities;

public sealed class GlobalShortcutObservers
{
    private readonly ObserverList<IGlobalShortcutObserver> _observers = new();

    public void Add(IGlobalShortcutObserver observer) => _observers.Add(observer);

    public void Remove(IGlobalShortcutObserver observer) => _observers.Remove(observer);

    public void Registered(in GlobalShortcutInfo shortcut)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.ShortcutRegistered(in shortcut);
        }

        _observers.EndDispatch();
    }

    public void Removed(in GlobalShortcutInfo shortcut)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.ShortcutRemoved(in shortcut);
        }

        _observers.EndDispatch();
    }
}
