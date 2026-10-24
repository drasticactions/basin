namespace Basin.Capabilities.Defaults;

/// <summary>
/// Reports a session that never locks. It is the default for a compositor without a lock screen.
/// </summary>
public sealed class NeverLocked : ILockState
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static NeverLocked Instance { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the session is locked. It is always <see langword="false"/>.
    /// </summary>
    public bool IsLocked => false;

    /// <summary>
    /// Accepts an observer and never notifies it.
    /// </summary>
    /// <param name="observer">The observer. It must not be <see langword="null"/>.</param>
    public void AddObserver(ILockStateObserver observer) => ArgumentNullException.ThrowIfNull(observer);

    /// <summary>
    /// Accepts the removal of an observer. It has no effect.
    /// </summary>
    /// <param name="observer">The observer. It must not be <see langword="null"/>.</param>
    public void RemoveObserver(ILockStateObserver observer) => ArgumentNullException.ThrowIfNull(observer);
}
