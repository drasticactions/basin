using Basin;
using Xunit;

namespace Westonia.Tests;

public sealed class BindingTests
{
    [Theory]
    [InlineData("super", ShellModifiers.Super)]
    [InlineData("Super", ShellModifiers.Super)]
    [InlineData("ctrl", ShellModifiers.Ctrl)]
    [InlineData("alt", ShellModifiers.Alt)]
    [InlineData("none", ShellModifiers.None)]
    [InlineData("nonsense", ShellModifiers.Super)]
    public void The_binding_modifier_resolves_from_weston_ini(string value, ShellModifiers expected) =>
        Assert.Equal(expected, BindingModifiers.Parse(value));

    [Fact]
    public void Modifiers_come_from_key_codes_rather_than_from_a_mask()
    {
        var state = new ShellModifierState();

        Assert.True(state.Track(InputCodes.KeyLeftMeta, pressed: true));
        Assert.True(state.Holds(ShellModifiers.Super));

        Assert.True(state.Track(InputCodes.KeyLeftShift, pressed: true));
        Assert.True(state.Holds(ShellModifiers.Super | ShellModifiers.Shift));
        Assert.False(state.Exactly(ShellModifiers.Super));

        Assert.True(state.Track(InputCodes.KeyLeftShift, pressed: false));
        Assert.True(state.Exactly(ShellModifiers.Super));

        Assert.True(state.Track(InputCodes.KeyLeftMeta, pressed: false));
        Assert.Equal(ShellModifiers.None, state.Current);
    }

    [Fact]
    public void Both_sides_of_a_modifier_count_as_the_same_modifier()
    {
        var state = new ShellModifierState();

        state.Track(InputCodes.KeyLeftCtrl, pressed: true);
        state.Track(InputCodes.KeyRightCtrl, pressed: true);
        state.Track(InputCodes.KeyLeftCtrl, pressed: false);

        Assert.True(state.Holds(ShellModifiers.Ctrl));

        state.Track(InputCodes.KeyRightCtrl, pressed: false);
        Assert.False(state.Holds(ShellModifiers.Ctrl));
    }

    [Fact]
    public void An_ordinary_key_is_not_a_modifier()
    {
        var state = new ShellModifierState();

        Assert.False(state.Track(InputCodes.KeyTab, pressed: true));
        Assert.Equal(ShellModifiers.None, state.Current);
    }

    [Fact]
    public void The_zap_chord_is_ctrl_alt_backspace_and_nothing_else()
    {
        var state = new ShellModifierState();
        state.Track(InputCodes.KeyLeftCtrl, pressed: true);
        state.Track(InputCodes.KeyLeftAlt, pressed: true);

        Assert.True(state.Exactly(ShellModifiers.Ctrl | ShellModifiers.Alt));

        state.Track(InputCodes.KeyLeftShift, pressed: true);
        Assert.False(state.Exactly(ShellModifiers.Ctrl | ShellModifiers.Alt));
    }

    [Fact]
    public void Carrying_a_window_avoids_the_tiled_orientation_chord()
    {
        var state = new ShellModifierState();
        state.Track(InputCodes.KeyLeftMeta, pressed: true);
        state.Track(InputCodes.KeyLeftShift, pressed: true);

        Assert.True(state.Holds(ShellModifiers.Super | ShellModifiers.Shift));
        Assert.False(state.Holds(ShellModifiers.Ctrl));

        state.Track(InputCodes.KeyLeftShift, pressed: false);
        state.Track(InputCodes.KeyLeftCtrl, pressed: true);

        Assert.True(state.Holds(ShellModifiers.Super | ShellModifiers.Ctrl));
        Assert.False(state.Holds(ShellModifiers.Shift));
    }

    [Fact]
    public void The_workspace_jump_keys_are_six_consecutive_function_keys()
    {
        Assert.Equal(59u, InputCodes.KeyF1);
        Assert.Equal(64u, InputCodes.KeyF1 + 5);
    }
}
