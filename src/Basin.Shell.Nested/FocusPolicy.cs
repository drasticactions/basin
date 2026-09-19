namespace Basin.Shell.Nested;

public static class FocusPolicy
{
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

    public static bool FocusFollowsEnter(FocusMode mode) => mode != FocusMode.Click;

    public static bool UnfocusOnLeaveToDesktop(FocusMode mode) => mode == FocusMode.Mouse;
}
