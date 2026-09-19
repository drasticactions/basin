using Basin.Shell.Nested;
using Xunit;

namespace Basin.Tests.Nested;

public sealed class FocusPolicyTests
{
    [Theory]
    [InlineData(FocusNewWindows.Smart)]
    [InlineData(FocusNewWindows.Strict)]
    public void A_window_takes_focus_when_nothing_is_focused(FocusNewWindows mode)
    {
        Assert.True(FocusPolicy.TakesFocusOnMap(mode, anyFocused: false, 100, 200, transientOfFocused: false));
    }

    [Fact]
    public void Strict_never_steals_from_the_focused_window()
    {
        Assert.False(FocusPolicy.TakesFocusOnMap(FocusNewWindows.Strict, anyFocused: true, 0, 0, transientOfFocused: false));
        Assert.False(FocusPolicy.TakesFocusOnMap(FocusNewWindows.Strict, anyFocused: true, 300, 200, transientOfFocused: false));
    }

    [Theory]
    [InlineData(FocusNewWindows.Smart)]
    [InlineData(FocusNewWindows.Strict)]
    public void A_transient_of_the_focused_window_always_takes_focus(FocusNewWindows mode)
    {
        Assert.True(FocusPolicy.TakesFocusOnMap(mode, anyFocused: true, 100, 200, transientOfFocused: true));
    }

    [Theory]
    [InlineData(100, 200, false)]
    [InlineData(200, 100, true)]
    [InlineData(200, 200, true)]
    [InlineData(0, 200, true)]
    [InlineData(100, 0, true)]
    public void Smart_denies_a_window_whose_user_time_is_older_than_the_last_interaction(long userTime, long lastInteraction, bool expected)
    {
        Assert.Equal(expected, FocusPolicy.TakesFocusOnMap(FocusNewWindows.Smart, anyFocused: true, userTime, lastInteraction, transientOfFocused: false));
    }

    [Theory]
    [InlineData(FocusMode.Click, false)]
    [InlineData(FocusMode.Sloppy, true)]
    [InlineData(FocusMode.Mouse, true)]
    public void Focus_follows_the_pointer_under_sloppy_and_mouse(FocusMode mode, bool expected)
    {
        Assert.Equal(expected, FocusPolicy.FocusFollowsEnter(mode));
    }

    [Theory]
    [InlineData(FocusMode.Click, false)]
    [InlineData(FocusMode.Sloppy, false)]
    [InlineData(FocusMode.Mouse, true)]
    public void Only_mouse_focus_drops_focus_when_the_pointer_leaves_to_the_desktop(FocusMode mode, bool expected)
    {
        Assert.Equal(expected, FocusPolicy.UnfocusOnLeaveToDesktop(mode));
    }
}
