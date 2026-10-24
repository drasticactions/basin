namespace Basin.Shell.Nested;

/// <summary>
/// Selects how a new window can take focus from the focused window when it maps.
/// </summary>
public enum FocusNewWindows
{
    /// <summary>
    /// A new window takes focus unless its user time is older than the last interaction with the focused window.
    /// </summary>
    Smart,
    /// <summary>
    /// A new window takes focus only when no window has focus or when it is a transient of the focused window.
    /// </summary>
    Strict,
}
