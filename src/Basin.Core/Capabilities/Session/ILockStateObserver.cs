namespace Basin.Capabilities;

/// <summary>
/// Receives lock and unlock changes from an <see cref="ILockState"/>.
/// </summary>
public interface ILockStateObserver
{
    /// <summary>
    /// Called after the session becomes locked.
    /// </summary>
    void SessionLocked();

    /// <summary>
    /// Called after the session becomes unlocked.
    /// </summary>
    void SessionUnlocked();
}
