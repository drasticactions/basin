namespace Basin.Shell.Nested;

/// <summary>
/// The focus rules of the shell as pure functions.
/// </summary>
public static class FocusPolicy
{
    /// <summary>
    /// Decides whether a window that maps takes focus from the focused window.
    /// A user time of zero counts as unknown, and an unknown time lets the new window take focus in <see cref="FocusNewWindows.Smart"/> mode.
    /// </summary>
    /// <param name="mode">The focus-stealing rule.</param>
    /// <param name="anyFocused">A value indicating whether a window has focus now.</param>
    /// <param name="newWindowUserTime">The user time of the new window in milliseconds, or zero when unknown.</param>
    /// <param name="focusedLastInteraction">The last interaction time of the focused window in milliseconds, or zero when unknown.</param>
    /// <param name="transientOfFocused">A value indicating whether the new window is a transient of the focused window.</param>
    /// <returns><see langword="true"/> when the new window takes focus.</returns>
    public static bool TakesFocusOnMap(FocusNewWindows mode, bool anyFocused, long newWindowUserTime, long focusedLastInteraction, bool transientOfFocused)
    {
        if (!anyFocused || transientOfFocused)
            return true;
        if (mode == FocusNewWindows.Strict)
            return false;
        if (newWindowUserTime == 0 || focusedLastInteraction == 0)
            return true;
        return newWindowUserTime >= focusedLastInteraction;
    }

    /// <summary>
    /// Decides whether the pointer entering a window focuses it.
    /// </summary>
    /// <param name="mode">The focus mode.</param>
    /// <returns><see langword="true"/> for every mode except <see cref="FocusMode.Click"/>.</returns>
    public static bool FocusFollowsEnter(FocusMode mode) => mode != FocusMode.Click;

    /// <summary>
    /// Decides whether the pointer moving onto the desktop clears focus.
    /// </summary>
    /// <param name="mode">The focus mode.</param>
    /// <returns><see langword="true"/> for <see cref="FocusMode.Mouse"/> only.</returns>
    public static bool UnfocusOnLeaveToDesktop(FocusMode mode) => mode == FocusMode.Mouse;
}
