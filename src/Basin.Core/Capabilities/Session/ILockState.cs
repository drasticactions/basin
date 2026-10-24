namespace Basin.Capabilities;

/// <summary>
/// Reports whether the session is locked. Lock-notify protocols read it.
/// </summary>
public interface ILockState
{
    /// <summary>
    /// Gets a value indicating whether a lock screen is presented on every output.
    /// </summary>
    bool IsLocked { get; }

    /// <summary>
    /// Adds an observer.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddObserver(ILockStateObserver observer);

    /// <summary>
    /// Removes an observer.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveObserver(ILockStateObserver observer);
}
