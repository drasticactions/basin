using Basin.Backend.Wayland;
using Xunit;

namespace Basin.Tests;

public class NestedKeyboardTests
{
    private static readonly NestedParentOptions Typing = NestedParentOptions.Undecorating with { Keyboard = true };

    private static List<string> Record(NestedBackendTestHost host)
    {
        var events = new List<string>();
        host.Backend.KeyboardAdded += keyboard =>
        {
            keyboard.Key += (_, key, pressed) => events.Add($"key {key} {(pressed ? "down" : "up")}");
            keyboard.Enter += _ => events.Add("enter");
            keyboard.Leave += () => events.Add("leave");
        };
        return events;
    }

    [Fact]
    public void A_key_held_when_the_parent_takes_focus_away_is_released_before_the_leave()
    {
        using var host = new NestedBackendTestHost(Typing);
        _ = host.CreateOutput();
        var events = Record(host);
        host.Pump();
        host.Parent.Invoke(() =>
        {
            host.Parent.Seat!.Keyboard.NotifyEnter(host.Parent.Toplevels[0].Surface);
            host.Parent.Seat!.Keyboard.NotifyKey(10, InputCodes.KeyLeftMeta, pressed: true);
        });
        host.Pump();
        host.Parent.Invoke(() => host.Parent.Seat!.Keyboard.NotifyClearFocus());
        host.Pump();

        Assert.Equal(["enter", "key 125 down", "key 125 up", "leave"], events);
    }

    [Fact]
    public void Keys_the_parent_reports_held_on_enter_are_not_pressed_and_their_release_is_dropped()
    {
        using var host = new NestedBackendTestHost(Typing);
        _ = host.CreateOutput();
        var events = Record(host);
        host.Pump();
        host.Parent.Invoke(() =>
        {
            host.Parent.Seat!.Keyboard.NotifyKey(10, InputCodes.KeyLeftShift, pressed: true);
            host.Parent.Seat!.Keyboard.NotifyEnter(host.Parent.Toplevels[0].Surface);
        });
        host.Pump();
        host.Parent.Invoke(() => host.Parent.Seat!.Keyboard.NotifyKey(20, InputCodes.KeyLeftShift, pressed: false));
        host.Pump();

        Assert.Equal(["enter"], events);
    }

    [Fact]
    public void A_key_held_across_a_leave_and_an_enter_is_released_once()
    {
        using var host = new NestedBackendTestHost(Typing);
        _ = host.CreateOutput();
        var events = Record(host);
        host.Pump();
        host.Parent.Invoke(() =>
        {
            host.Parent.Seat!.Keyboard.NotifyEnter(host.Parent.Toplevels[0].Surface);
            host.Parent.Seat!.Keyboard.NotifyKey(10, InputCodes.KeyLeftAlt, pressed: true);
        });
        host.Pump();
        host.Parent.Invoke(() => host.Parent.Seat!.Keyboard.NotifyClearFocus());
        host.Pump();
        host.Parent.Invoke(() => host.Parent.Seat!.Keyboard.NotifyEnter(host.Parent.Toplevels[0].Surface));
        host.Pump();
        host.Parent.Invoke(() => host.Parent.Seat!.Keyboard.NotifyKey(20, InputCodes.KeyLeftAlt, pressed: false));
        host.Pump();

        Assert.Equal(["enter", "key 56 down", "key 56 up", "leave", "enter"], events);
    }

    [Fact]
    public void A_keyboard_capability_the_parent_drops_releases_its_keys_and_a_new_one_binds_again()
    {
        using var host = new NestedBackendTestHost(Typing);
        _ = host.CreateOutput();
        var events = Record(host);
        var keyboards = new List<WaylandKeyboardDevice>();
        host.Backend.KeyboardAdded += keyboards.Add;
        host.Backend.KeyboardRemoved += _ => events.Add("removed");
        host.Pump();
        host.Parent.Invoke(() =>
        {
            host.Parent.Seat!.Keyboard.NotifyEnter(host.Parent.Toplevels[0].Surface);
            host.Parent.Seat!.Keyboard.NotifyKey(10, InputCodes.KeyS, pressed: true);
            host.Parent.Seat!.Keyboard.NotifyKey(11, InputCodes.KeyF, pressed: true);
        });
        host.Pump();
        host.Parent.Invoke(() => host.Parent.Seat!.SetCapability(Basin.Seat.SeatCapability.Keyboard, false));
        host.Pump();
        host.Parent.Invoke(() => host.Parent.Seat!.SetCapability(Basin.Seat.SeatCapability.Keyboard, true));
        host.Pump();

        Assert.Equal(["enter", "key 31 down", "key 33 down", "key 33 up", "key 31 up", "leave", "removed"], events);
        Assert.Equal(2, keyboards.Count);
        Assert.NotSame(keyboards[0], keyboards[1]);
        Assert.Same(keyboards[1], host.Backend.Keyboard);
    }
}
