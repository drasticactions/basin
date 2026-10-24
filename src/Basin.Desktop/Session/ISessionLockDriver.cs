namespace Basin.Desktop;

/// <summary>
/// Puts lock surfaces on screen and moves focus for a <see cref="SessionLockManager"/>. The session lock module requires one.
/// </summary>
public interface ISessionLockDriver
{
    /// <summary>
    /// Starts to drive a lock manager. The module calls it once, when it installs the global.
    /// </summary>
    /// <param name="manager">The manager to follow.</param>
    void Attach(SessionLockManager manager);
}
