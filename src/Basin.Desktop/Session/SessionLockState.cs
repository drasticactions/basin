using Basin.Capabilities;

namespace Basin.Desktop;

/// <summary>
/// Exposes the presented lock state of a <see cref="SessionLockManager"/> as <see cref="ILockState"/>.
/// </summary>
public sealed class SessionLockState : ILockState
{
    private readonly LockStateObservers _observers = new();
    private SessionLockManager? _manager;

    /// <inheritdoc/>
    public bool IsLocked => _manager?.IsPresentedLocked ?? false;

    /// <summary>
    /// Follows a lock manager, and stops following the previous one.
    /// </summary>
    /// <param name="manager">The manager to follow.</param>
    /// <exception cref="ArgumentNullException"><paramref name="manager"/> is <see langword="null"/>.</exception>
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

    /// <inheritdoc/>
    public void AddObserver(ILockStateObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(ILockStateObserver observer) => _observers.Remove(observer);

    private void OnLocked() => _observers.Locked();

    private void OnUnlocked() => _observers.Unlocked();
}
