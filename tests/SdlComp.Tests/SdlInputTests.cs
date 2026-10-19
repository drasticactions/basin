using Basin;
using Basin.Hosted;
using Basin.Sdl;
using Basin.Seat;
using SDL3;
using Wayland;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlInputTests
{
    private static List<BasinViewInput> Record(SdlCompHarness harness)
    {
        var seen = new List<BasinViewInput>();
        var shell = harness.Comp.Shell;
        harness.Comp.Window.InputSink = input =>
        {
            seen.Add(input);
            shell.HandleInput(input);
        };
        return seen;
    }

    [Fact]
    public void Every_scancode_in_the_table_reaches_an_evdev_code()
    {
        foreach (var (scancode, code) in SdlKeyMap.Entries)
        {
            Assert.True(HostKeyMap.TryToEvdev(code, out _), $"{scancode} maps to {code}, which has no evdev code");
        }

        Assert.True(SdlKeyMap.TryToEvdev(SDL.Scancode.A, out var a));
        Assert.Equal(30u, a);
        Assert.True(SdlKeyMap.TryToEvdev(SDL.Scancode.NonUsHash, out var hash));
        Assert.Equal(43u, hash);
        Assert.True(SdlKeyMap.TryToEvdev(SDL.Scancode.NonUsBackSlash, out var iso));
        Assert.Equal(86u, iso);
        Assert.True(SdlKeyMap.TryToEvdev(SDL.Scancode.RAlt, out var altGr));
        Assert.Equal(100u, altGr);
        Assert.True(SdlKeyMap.TryToEvdev(SDL.Scancode.International1, out var ro));
        Assert.Equal(89u, ro);
    }

    [Fact]
    public void Keys_map_and_repeats_are_dropped()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var seen = Record(harness);
        var id = harness.Comp.Window.Window.Id;
        SdlEvents.Key(id, SDL.Scancode.A, true);
        SdlEvents.Key(id, SDL.Scancode.A, true, repeat: true);
        SdlEvents.Key(id, SDL.Scancode.A, true, repeat: true);
        SdlEvents.Key(id, SDL.Scancode.A, false);
        SdlEvents.Key(id + 100, SDL.Scancode.B, true);
        harness.PumpUntil(() => seen.Count(i => i.Kind == BasinViewInputKind.Key) >= 2, "the keys never reached the shell");
        harness.Pump();

        var keys = seen.Where(i => i.Kind == BasinViewInputKind.Key).ToList();
        Assert.Equal(2, keys.Count);
        Assert.Equal((30u, true), (keys[0].Code, keys[0].Pressed));
        Assert.Equal((30u, false), (keys[1].Code, keys[1].Pressed));
    }

    [Fact]
    public void Buttons_follow_the_SDL_order_and_coordinates_are_logical()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, scale: 2.0);
        var seen = Record(harness);
        var id = harness.Comp.Window.Window.Id;
        SdlEvents.Button(id, 2, true, 100, 60);
        SdlEvents.Button(id, 2, false, 100, 60);
        SdlEvents.Button(id, 3, true, 100, 60);
        SdlEvents.Button(id, 4, true, 100, 60);
        SdlEvents.Button(id, 5, true, 100, 60);
        harness.PumpUntil(() => seen.Count(i => i.Kind == BasinViewInputKind.PointerButton) >= 5, "the buttons never arrived");

        var buttons = seen.Where(i => i.Kind == BasinViewInputKind.PointerButton).Select(i => (i.Code, i.Pressed)).ToList();
        Assert.Equal([(InputCodes.BtnMiddle, true), (InputCodes.BtnMiddle, false), (InputCodes.BtnRight, true), (InputCodes.BtnSide, true), (InputCodes.BtnExtra, true)], buttons);
        var press = seen.First(i => i.Kind == BasinViewInputKind.PointerButton);
        Assert.Equal((50.0, 30.0), (press.X, press.Y));
    }

    [Fact]
    public void A_wheel_notch_reaches_a_client_as_value120()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var seat = harness.Client.Seat!;
        var pointer = seat.GetPointer();
        var value120 = new List<(WlPointer.Axis Axis, int Value)>();
        var entered = false;
        pointer.Enter += (_, _) => entered = true;
        pointer.AxisValue120 += (_, e) => value120.Add((e.Axis, e.Value120));
        harness.MapToplevel(200, 150, (data, stride) => { });
        var window = harness.Comp.Shell.Windows[0].ClientBox;
        var id = harness.Comp.Window.Window.Id;
        SdlEvents.Motion(id, window.X + 50, window.Y + 50);
        harness.PumpUntil(() => entered, "the pointer never entered the client");

        SdlEvents.Wheel(id, 0, 1);
        SdlEvents.Wheel(id, 0, 1, flipped: true);
        SdlEvents.Wheel(id, 1, 0);
        harness.PumpUntil(() => value120.Count >= 3, "the client never saw the wheel");
        Assert.Equal((WlPointer.Axis.VerticalScroll, -120), value120[0]);
        Assert.Equal((WlPointer.Axis.VerticalScroll, 120), value120[1]);
        Assert.Equal((WlPointer.Axis.HorizontalScroll, 120), value120[2]);
    }

    [Fact]
    public void Enter_leave_and_focus_become_view_input()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var seen = Record(harness);
        var id = harness.Comp.Window.Window.Id;
        SdlEvents.Window(id, SDL.EventType.WindowMouseEnter);
        SdlEvents.Window(id, SDL.EventType.WindowMouseLeave);
        SdlEvents.Window(id, SDL.EventType.WindowFocusGained);
        SdlEvents.Window(id, SDL.EventType.WindowFocusLost);
        harness.PumpUntil(() => seen.Count >= 4, "the window events never arrived");
        Assert.Equal(
            [BasinViewInputKind.PointerEnter, BasinViewInputKind.PointerLeave, BasinViewInputKind.FocusIn, BasinViewInputKind.FocusOut],
            seen.Select(i => i.Kind).Take(4));
    }

    [Fact]
    public void A_key_reaches_the_focused_client()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var keyboard = harness.Client.Seat!.GetKeyboard();
        var keys = new List<(uint Key, WlKeyboard.KeyState State)>();
        var focused = false;
        keyboard.Enter += (_, _) => focused = true;
        keyboard.Key += (_, e) => keys.Add((e.Key, e.State));
        harness.MapToplevel(200, 150, (data, stride) => { });
        var id = harness.Comp.Window.Window.Id;
        SdlEvents.Window(id, SDL.EventType.WindowFocusGained);
        harness.PumpUntil(() => focused, "the client never got keyboard focus");
        SdlEvents.Key(id, SDL.Scancode.Q, true);
        SdlEvents.Key(id, SDL.Scancode.Q, false);
        harness.PumpUntil(() => keys.Count >= 2, "the client never saw the key");
        Assert.Equal([(16u, WlKeyboard.KeyState.Pressed), (16u, WlKeyboard.KeyState.Released)], keys);
    }
}
