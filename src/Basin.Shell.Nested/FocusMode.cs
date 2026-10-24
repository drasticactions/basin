namespace Basin.Shell.Nested;

/// <summary>
/// Selects how the pointer moves keyboard focus between windows.
/// </summary>
public enum FocusMode
{
    /// <summary>
    /// A click focuses a window. Pointer motion alone does not change focus.
    /// </summary>
    Click,
    /// <summary>
    /// The pointer focuses the window it enters. Focus stays when the pointer moves onto the desktop.
    /// </summary>
    Sloppy,
    /// <summary>
    /// The pointer focuses the window it enters. Focus clears when the pointer moves onto the desktop.
    /// </summary>
    Mouse,
}
