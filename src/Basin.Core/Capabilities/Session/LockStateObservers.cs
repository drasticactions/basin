namespace Basin.Capabilities;

/// <summary>
/// Holds a list of <see cref="ILockStateObserver"/> instances for an implementation of <see cref="ILockState"/>. Observers can add or remove observers during a dispatch.
/// </summary>
public sealed class LockStateObservers
{
    private readonly ObserverList<ILockStateObserver> _observers = new();

    /// <summary>
    /// Adds an observer.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    public void Add(ILockStateObserver observer) => _observers.Add(observer);

    /// <summary>
    /// Removes an observer.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    public void Remove(ILockStateObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Tells each observer that the session is locked.
    /// </summary>
    public void Locked()
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.SessionLocked();
        }

        _observers.EndDispatch();
    }

    /// <summary>
    /// Tells each observer that the session is unlocked.
    /// </summary>
    public void Unlocked()
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.SessionUnlocked();
        }

        _observers.EndDispatch();
    }
}
