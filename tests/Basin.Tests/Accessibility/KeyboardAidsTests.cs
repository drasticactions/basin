using Basin.Capabilities;
using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public sealed class KeyboardAidsTests
{
    private const uint Shift = 42;
    private const uint Control = 29;
    private const uint A = 30;
    private const uint B = 48;

    private sealed class Spoken : IAnnouncer
    {
        public List<string> Lines { get; } = [];

        public void Announce(string text, AnnouncePriority priority = AnnouncePriority.Polite) => Lines.Add(text);
    }

    private sealed class FakePointer : ISyntheticInput
    {
        public double X { get; private set; } = 100;

        public double Y { get; private set; } = 100;

        public List<(uint Button, bool Pressed)> Buttons { get; } = [];

        public bool PointerMotionAbsolute(uint timeMs, double x, double y)
        {
            (X, Y) = (x, y);
            return true;
        }

        public bool PointerButton(uint timeMs, uint button, bool pressed)
        {
            Buttons.Add((button, pressed));
            return true;
        }

        public bool Key(uint timeMs, uint keycode, bool pressed) => true;

        public bool TryPointerPosition(out double x, out double y)
        {
            (x, y) = (X, Y);
            return true;
        }
    }

    private static (KeyboardAids Aids, List<(uint Time, uint Key, bool Pressed)> Out, Spoken Voice) Make(KeyboardAidOptions options)
    {
        var delivered = new List<(uint, uint, bool)>();
        var voice = new Spoken();
        var aids = new KeyboardAids((time, key, pressed) => delivered.Add((time, key, pressed))) { Options = options, Announcer = voice };
        return (aids, delivered, voice);
    }

    private static void Tap(KeyboardAids aids, uint time, uint key)
    {
        aids.Process(time, key, true);
        aids.Process(time + 10, key, false);
    }

    [Fact]
    public void A_tapped_modifier_latches_for_the_next_key_and_a_second_tap_locks_it()
    {
        var (aids, delivered, voice) = Make(new KeyboardAidOptions { Sticky = true });
        Tap(aids, 0, Shift);
        Assert.Equal([(0u, Shift, true)], delivered);
        Tap(aids, 100, A);
        Assert.Equal([(0u, Shift, true), (100u, A, true), (110u, A, false), (110u, Shift, false)], delivered);
        Assert.Equal(["Shift latched"], voice.Lines);

        delivered.Clear();
        Tap(aids, 200, Shift);
        Tap(aids, 300, Shift);
        Tap(aids, 400, A);
        Tap(aids, 500, B);
        Assert.Equal([(200u, Shift, true), (400u, A, true), (410u, A, false), (500u, B, true), (510u, B, false)], delivered);
        Tap(aids, 600, Shift);
        Assert.Equal((610u, Shift, false), delivered[^1]);
        Assert.Equal(["Shift latched", "Shift latched", "Shift locked", "Shift released"], voice.Lines);
    }

    [Fact]
    public void A_held_chord_does_not_latch_and_two_latched_modifiers_both_apply()
    {
        var (aids, delivered, _) = Make(new KeyboardAidOptions { Sticky = true });
        aids.Process(0, Control, true);
        Tap(aids, 10, A);
        aids.Process(30, Control, false);
        Assert.Equal([(0u, Control, true), (10u, A, true), (20u, A, false), (30u, Control, false)], delivered);

        delivered.Clear();
        Tap(aids, 100, Control);
        Tap(aids, 200, Shift);
        Tap(aids, 300, A);
        Assert.Equal(6, delivered.Count);
        Assert.Equal([(100u, Control, true), (200u, Shift, true), (300u, A, true), (310u, A, false)], delivered[..4]);
        Assert.Contains((310u, Control, false), delivered);
        Assert.Contains((310u, Shift, false), delivered);
    }

    [Fact]
    public void Five_taps_of_shift_turn_sticky_keys_on_and_off_and_off_releases_a_lock()
    {
        var (aids, delivered, voice) = Make(new KeyboardAidOptions { StickyShortcut = true });
        KeyboardAidOptions? changed = null;
        aids.OptionsChanged += options => changed = options;
        for (var i = 0u; i < 5; i++)
        {
            Tap(aids, i * 100, Shift);
        }

        Assert.True(aids.Options.Sticky);
        Assert.True(changed?.Sticky);
        Assert.Equal("Sticky keys on", voice.Lines[^1]);

        delivered.Clear();
        Tap(aids, 1000, Shift);
        Tap(aids, 1100, Shift);
        aids.Options = aids.Options with { Sticky = false };
        Assert.Equal([(1000u, Shift, true)], delivered[..1]);
        Assert.Contains(delivered, d => d.Key == Shift && !d.Pressed);
    }

    [Fact]
    public void Slow_keys_accept_a_key_held_long_enough_and_drop_a_short_press()
    {
        var (aids, delivered, _) = Make(new KeyboardAidOptions { SlowMs = 300 });
        aids.Process(0, A, true);
        aids.Process(100, A, false);
        Assert.Empty(delivered);

        aids.Process(1000, A, true);
        aids.Advance(1200);
        Assert.Empty(delivered);
        aids.Advance(1300);
        Assert.Equal([(1300u, A, true)], delivered);
        aids.Process(1400, A, false);
        Assert.Equal([(1300u, A, true), (1400u, A, false)], delivered);

        delivered.Clear();
        aids.Process(2000, B, true);
        aids.Process(2500, B, false);
        Assert.Equal([(2300u, B, true), (2500u, B, false)], delivered);
    }

    [Fact]
    public void Bounce_keys_drop_a_press_that_follows_a_release_too_soon()
    {
        var (aids, delivered, _) = Make(new KeyboardAidOptions { BounceMs = 300 });
        Tap(aids, 0, A);
        Tap(aids, 100, A);
        Tap(aids, 150, B);
        Tap(aids, 500, A);
        Assert.Equal([(0u, A, true), (10u, A, false), (150u, B, true), (160u, B, false), (500u, A, true), (510u, A, false)], delivered);
    }

    [Fact]
    public void Mouse_keys_move_the_pointer_faster_while_held_and_keypad_five_clicks_the_chosen_button()
    {
        var pointer = new FakePointer();
        var (aids, delivered, _) = Make(new KeyboardAidOptions { MouseKeys = true, MouseKeysMaxSpeed = 1000, MouseKeysAccelerationMs = 1000 });
        aids.Pointer = pointer;
        aids.Process(0, 77, true);
        Assert.Equal(104, pointer.X);
        aids.Advance(100);
        var early = pointer.X - 104;
        aids.Advance(900);
        aids.Advance(1000);
        var late = pointer.X - 104 - early;
        Assert.True(early is > 0 and < 20, $"early {early}");
        Assert.True(late > 400, $"late {late}");
        aids.Process(1000, 77, false);
        var x = pointer.X;
        aids.Advance(1500);
        Assert.Equal(x, pointer.X);
        Assert.Equal(100, pointer.Y);

        Tap(aids, 2000, 76);
        aids.Process(2100, 74, true);
        aids.Process(2110, 74, false);
        Tap(aids, 2200, 76);
        Assert.Equal([(0x110u, true), (0x110u, false), (0x111u, true), (0x111u, false)], pointer.Buttons);
        Assert.Empty(delivered);

        Tap(aids, 3000, A);
        Assert.Equal([(3000u, A, true), (3010u, A, false)], delivered);
    }

    [Fact]
    public void With_every_aid_off_keys_pass_in_order_and_the_aids_are_idle()
    {
        var (aids, delivered, _) = Make(new KeyboardAidOptions());
        Assert.False(aids.IsActive);
        aids.Process(0, Shift, true);
        Tap(aids, 10, A);
        aids.Process(30, Shift, false);
        Assert.Equal([(0u, Shift, true), (10u, A, true), (20u, A, false), (30u, Shift, false)], delivered);
    }

    [Fact]
    public void The_keyboard_table_reads_each_aid_and_keeps_every_aid_off_without_it()
    {
        static KeyboardAidOptions Read(string toml) =>
            Basin.Config.AccessibilitySettings.Read(new Basin.Config.TomlReader(Tomlyn.Toml.ToModel(toml), Basin.Diagnostics.BasinLog.For("test")).Section("accessibility")).Keyboard;

        Assert.Equal(new KeyboardAidOptions(), Read("[accessibility]\nenabled = \"on\"\n"));
        var aids = Read("[accessibility.keyboard]\nsticky = true\nsticky-shortcut = true\nslow-ms = 250\nbounce-ms = 400\nmouse-keys = true\nmouse-keys-speed = 600\n");
        Assert.Equal(new KeyboardAidOptions { Sticky = true, StickyShortcut = true, SlowMs = 250, BounceMs = 400, MouseKeys = true, MouseKeysMaxSpeed = 600 }, aids);
        Assert.Equal(0, Read("[accessibility.keyboard]\nslow-ms = -5\n").SlowMs);
    }
}
