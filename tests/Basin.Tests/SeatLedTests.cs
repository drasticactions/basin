using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class SeatLedTests
{
    private const uint CapsKey = 58;
    private const uint NumKey = 69;
    private const uint ScrollKey = 70;

    [Fact]
    public void Caps_lock_sets_the_led_and_a_second_toggle_clears_it()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymap();
        var changes = 0;
        keyboard.LedsChanged += () => changes++;

        Assert.Equal(Basin.Seat.KeyboardLeds.None, keyboard.Leds);

        keyboard.NotifyKey(10, CapsKey, WlKeyboard.KeyState.Pressed);
        keyboard.NotifyKey(20, CapsKey, WlKeyboard.KeyState.Released);
        Assert.Equal(Basin.Seat.KeyboardLeds.CapsLock, keyboard.Leds);
        Assert.Equal(1, changes);

        keyboard.NotifyKey(30, CapsKey, WlKeyboard.KeyState.Pressed);
        keyboard.NotifyKey(40, CapsKey, WlKeyboard.KeyState.Released);
        Assert.Equal(Basin.Seat.KeyboardLeds.None, keyboard.Leds);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Num_lock_sets_its_own_led_beside_caps()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymap();

        keyboard.NotifyKey(10, NumKey, WlKeyboard.KeyState.Pressed);
        keyboard.NotifyKey(20, NumKey, WlKeyboard.KeyState.Released);
        Assert.Equal(Basin.Seat.KeyboardLeds.NumLock, keyboard.Leds);

        keyboard.NotifyKey(30, CapsKey, WlKeyboard.KeyState.Pressed);
        keyboard.NotifyKey(40, CapsKey, WlKeyboard.KeyState.Released);
        Assert.Equal(Basin.Seat.KeyboardLeds.NumLock | Basin.Seat.KeyboardLeds.CapsLock, keyboard.Leds);
    }

    [Fact]
    public void Scroll_lock_lights_under_a_keymap_that_locks_it()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(TestKeymaps.ScrollLock));

        keyboard.NotifyKey(10, ScrollKey, WlKeyboard.KeyState.Pressed);
        keyboard.NotifyKey(20, ScrollKey, WlKeyboard.KeyState.Released);
        Assert.Equal(Basin.Seat.KeyboardLeds.ScrollLock, keyboard.Leds);
    }

    [Fact]
    public void A_keymap_change_recomputes_the_leds_from_the_fresh_state()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymap();

        keyboard.NotifyKey(10, CapsKey, WlKeyboard.KeyState.Pressed);
        keyboard.NotifyKey(20, CapsKey, WlKeyboard.KeyState.Released);
        Assert.Equal(Basin.Seat.KeyboardLeds.CapsLock, keyboard.Leds);

        var changes = 0;
        keyboard.LedsChanged += () => changes++;
        keyboard.SetKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(TestKeymaps.ScrollLock));
        Assert.Equal(Basin.Seat.KeyboardLeds.None, keyboard.Leds);
        Assert.Equal(1, changes);
    }
}
