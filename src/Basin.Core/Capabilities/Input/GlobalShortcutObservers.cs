namespace Basin.Capabilities;

/// <summary>
/// A list of <see cref="IGlobalShortcutObserver"/> instances and the methods that notify all of them.
/// An observer can add or remove observers during a notification.
/// </summary>
public sealed class GlobalShortcutObservers
{
    private readonly ObserverList<IGlobalShortcutObserver> _observers = new();

    /// <summary>Adds an observer to the end of the list.</summary>
    /// <param name="observer">The observer to notify.</param>
    public void Add(IGlobalShortcutObserver observer) => _observers.Add(observer);

    /// <summary>Removes an observer. An observer that is not in the list is ignored.</summary>
    /// <param name="observer">The observer to remove.</param>
    public void Remove(IGlobalShortcutObserver observer) => _observers.Remove(observer);

    /// <summary>Calls <see cref="IGlobalShortcutObserver.ShortcutRegistered"/> on each observer.</summary>
    /// <param name="shortcut">The shortcut that was added.</param>
    public void Registered(in GlobalShortcutInfo shortcut)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.ShortcutRegistered(in shortcut);
        }

        _observers.EndDispatch();
    }

    /// <summary>Calls <see cref="IGlobalShortcutObserver.ShortcutRemoved"/> on each observer.</summary>
    /// <param name="shortcut">The shortcut that was removed.</param>
    public void Removed(in GlobalShortcutInfo shortcut)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.ShortcutRemoved(in shortcut);
        }

        _observers.EndDispatch();
    }

    /// <summary>Calls <see cref="IGlobalShortcutObserver.ShortcutActivated"/> on each observer.</summary>
    /// <param name="shortcut">The shortcut that was pressed.</param>
    /// <param name="timestampMs">The time of the press, in milliseconds on a monotonic clock.</param>
    public void Activated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.ShortcutActivated(in shortcut, timestampMs);
        }

        _observers.EndDispatch();
    }

    /// <summary>Calls <see cref="IGlobalShortcutObserver.ShortcutDeactivated"/> on each observer.</summary>
    /// <param name="shortcut">The shortcut that was released.</param>
    /// <param name="timestampMs">The time of the release, in milliseconds on a monotonic clock.</param>
    public void Deactivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.ShortcutDeactivated(in shortcut, timestampMs);
        }

        _observers.EndDispatch();
    }
}
