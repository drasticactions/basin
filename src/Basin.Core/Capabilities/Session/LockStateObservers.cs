namespace Basin.Capabilities;

public sealed class LockStateObservers
{
    private readonly ObserverList<ILockStateObserver> _observers = new();

    public void Add(ILockStateObserver observer) => _observers.Add(observer);

    public void Remove(ILockStateObserver observer) => _observers.Remove(observer);

    public void Locked()
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.SessionLocked();
        }

        _observers.EndDispatch();
    }

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
