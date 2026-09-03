using Basin.Capabilities;

namespace Basin.Desktop;

public sealed class SessionLockState : ILockState
{
    private readonly LockStateObservers _observers = new();
    private SessionLockManager? _manager;

    public bool IsLocked => _manager?.IsPresentedLocked ?? false;

    public void Attach(SessionLockManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (_manager is { } previous)
        {
            previous.Locked -= OnLocked;
            previous.Unlocked -= OnUnlocked;
        }

        _manager = manager;
        manager.Locked += OnLocked;
        manager.Unlocked += OnUnlocked;
    }

    public void AddObserver(ILockStateObserver observer) => _observers.Add(observer);

    public void RemoveObserver(ILockStateObserver observer) => _observers.Remove(observer);

    private void OnLocked() => _observers.Locked();

    private void OnUnlocked() => _observers.Unlocked();
}
