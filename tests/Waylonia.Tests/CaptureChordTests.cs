using Basin.Diagnostics;
using Waylonia;
using Xunit;

namespace Waylonia.Tests;

public sealed class CaptureChordTests
{
    private static CaptureChord? Parse(string text) => CaptureChord.Parse(text, BasinLogger.None);

    [Fact]
    public void The_default_chord_is_a_double_tap_of_the_right_control()
    {
        var chord = Assert.IsType<CaptureChord>(Parse("double:RightControl"));

        Assert.True(chord.DoubleTap);
        Assert.Equal(97u, chord.Code);
        Assert.Equal(HotkeyModifiers.None, chord.Modifiers);
        Assert.Equal("double:RightControl", chord.Text);
    }

    [Theory]
    [InlineData("rightcontrol", 97u)]
    [InlineData("rctrl", 97u)]
    [InlineData("ControlRight", 97u)]
    [InlineData("leftalt", 56u)]
    [InlineData("super", 125u)]
    [InlineData("rightsuper", 126u)]
    [InlineData("escape", 1u)]
    [InlineData("g", 34u)]
    [InlineData("1", 2u)]
    [InlineData("0", 11u)]
    [InlineData("f5", 63u)]
    public void Every_spelling_reaches_the_same_evdev_code(string name, uint expected)
    {
        Assert.Equal(expected, CaptureChord.CodeFor(name));
    }

    [Fact]
    public void A_combination_carries_its_modifiers_and_its_key()
    {
        var chord = Assert.IsType<CaptureChord>(Parse("Ctrl+Alt+G"));

        Assert.False(chord.DoubleTap);
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, chord.Modifiers);
        Assert.Equal(34u, chord.Code);
    }

    [Fact]
    public void A_double_tap_names_one_key_alone_and_its_modifiers_are_dropped()
    {
        var chord = Assert.IsType<CaptureChord>(Parse("double:ctrl+rightalt"));

        Assert.True(chord.DoubleTap);
        Assert.Equal(HotkeyModifiers.None, chord.Modifiers);
        Assert.Equal(100u, chord.Code);
    }

    [Fact]
    public void An_unknown_key_turns_capture_off_rather_than_guessing()
    {
        Assert.Null(Parse("double:Hyper_L"));
    }

    [Fact]
    public void An_unknown_modifier_turns_capture_off()
    {
        Assert.Null(Parse("hyper+g"));
    }

    [Fact]
    public void An_empty_chord_turns_capture_off()
    {
        Assert.Null(Parse("   "));
    }

    [Theory]
    [InlineData(42u, nameof(HotkeyModifiers.Shift))]
    [InlineData(54u, nameof(HotkeyModifiers.Shift))]
    [InlineData(29u, nameof(HotkeyModifiers.Ctrl))]
    [InlineData(97u, nameof(HotkeyModifiers.Ctrl))]
    [InlineData(56u, nameof(HotkeyModifiers.Alt))]
    [InlineData(100u, nameof(HotkeyModifiers.Alt))]
    [InlineData(125u, nameof(HotkeyModifiers.Super))]
    [InlineData(126u, nameof(HotkeyModifiers.Super))]
    [InlineData(34u, nameof(HotkeyModifiers.None))]
    public void Both_sides_of_a_modifier_carry_the_same_flag(uint code, string expected)
    {
        Assert.Equal(Enum.Parse<HotkeyModifiers>(expected), CaptureChord.ModifierOf(code));
    }
}
