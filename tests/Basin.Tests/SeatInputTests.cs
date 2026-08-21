using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class SeatInputTests
{
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close")]
    private static extern int LibcClose(int fd);

    [Fact]
    public void Keyboard_delivers_keymap_enter_keys_and_modifiers()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        var keyboard = client.Seat!.GetKeyboard();
        (uint Format, uint Size)? keymap = null;
        var entered = false;
        var keys = new List<(uint Key, uint State)>();
        (uint Depressed, uint Group)? modifiers = null;
        keyboard.Keymap += (_, e) =>
        {
            keymap = ((uint)e.Format, e.Size);
            LibcClose(e.Fd);
        };
        keyboard.Enter += (_, _) => entered = true;
        keyboard.Key += (_, e) => keys.Add((e.Key, (uint)e.State));
        keyboard.Modifiers += (_, e) => modifiers = (e.ModsDepressed, e.Group);
        host.PumpUntil(() => keymap is not null);
        Assert.True(keymap!.Value.Size > 0);

        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpUntil(() => entered && modifiers is not null);

        host.Seat.Keyboard.NotifyKey(10, 38, WlKeyboard.KeyState.Pressed);
        host.Seat.Keyboard.NotifyKey(20, 38, WlKeyboard.KeyState.Released);
        host.PumpUntil(() => keys.Count == 2);
        Assert.Equal((38u, 1u), keys[0]);
        Assert.Equal((38u, 0u), keys[1]);

        modifiers = null;
        host.Seat.Keyboard.NotifyKey(30, 42, WlKeyboard.KeyState.Pressed);
        host.PumpUntil(() => modifiers is not null);
        Assert.NotEqual(0u, modifiers!.Value.Depressed);
        host.Seat.Keyboard.NotifyKey(40, 42, WlKeyboard.KeyState.Released);
        host.PumpToClient();
    }

    [Fact]
    public void Pointer_round_trip_with_frames_and_serial_kinds()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        var pointer = client.Seat!.GetPointer();
        var log = new List<string>();
        uint pressSerial = 0;
        pointer.Enter += (_, e) => log.Add($"enter {e.SurfaceX.ToDouble():F0},{e.SurfaceY.ToDouble():F0}");
        pointer.Motion += (_, e) => log.Add($"motion {e.SurfaceX.ToDouble():F0},{e.SurfaceY.ToDouble():F0}");
        pointer.Button += (_, e) =>
        {
            log.Add($"button {e.Button} {(uint)e.State}");
            if (e.State == WlPointer.ButtonState.Pressed)
            {
                pressSerial = e.Serial;
            }
        };
        pointer.Leave += (_, _) => log.Add("leave");
        host.PumpToClient();

        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 4, 5);
        host.Seat.Pointer.NotifyMotion(1, 6, 7);
        var serverPressSerial = host.Seat.Pointer.NotifyButton(2, 0x110, WlPointer.ButtonState.Pressed);

        Assert.True(host.Seat.Pointer.HasImplicitGrab);
        Assert.True(host.Seat.ValidateImplicitGrabSerial(serverPressSerial));
        host.Seat.Pointer.NotifyButton(3, 0x110, WlPointer.ButtonState.Released);
        Assert.False(host.Seat.Pointer.HasImplicitGrab);
        Assert.False(host.Seat.ValidateImplicitGrabSerial(serverPressSerial));
        Assert.True(host.Seat.ValidateGrabSerial(serverPressSerial));

        host.Seat.Pointer.NotifyClearFocus();
        host.PumpUntil(() => log.Count == 5);

        Assert.Equal(["enter 4,5", "motion 6,7", "button 272 1", "button 272 0", "leave"], log);
        Assert.Equal(serverPressSerial, pressSerial);
        Assert.True(host.Seat.ValidateGrabSerial(pressSerial));
        Assert.False(host.Seat.ValidateGrabSerial(pressSerial + 1000));
    }

    [Fact]
    public void A_lost_release_is_cleared_without_reaching_the_client()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var keyboard = client.Seat!.GetKeyboard();
        var pointer = client.Seat!.GetPointer();
        var keys = new List<(uint Key, uint State)>();
        var buttons = new List<(uint Button, uint State)>();
        var entered = new List<int>();
        keyboard.Keymap += (_, e) => LibcClose(e.Fd);
        keyboard.Key += (_, e) => keys.Add((e.Key, (uint)e.State));
        keyboard.Enter += (_, e) => entered.Add(e.Keys.Length / 4);
        pointer.Button += (_, e) => buttons.Add((e.Button, (uint)e.State));
        host.PumpToClient();

        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.Seat.Keyboard.NotifyKey(10, 28, WlKeyboard.KeyState.Pressed);
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 1, 1);
        host.Seat.Pointer.NotifyButton(11, 0x110, WlPointer.ButtonState.Pressed);
        host.PumpUntil(() => keys.Count == 1 && buttons.Count == 1);
        Assert.Single(host.Seat.Keyboard.PressedKeys);
        Assert.True(host.Seat.Pointer.HasImplicitGrab);

        host.Seat.Keyboard.NotifyKeyConsumed(28, pressed: false);
        host.Seat.Pointer.ClearImplicitGrab();
        host.PumpToClient();

        Assert.Empty(host.Seat.Keyboard.PressedKeys);
        Assert.False(host.Seat.Pointer.HasImplicitGrab);
        Assert.Equal([(28u, 1u)], keys);
        Assert.Equal([(0x110u, 1u)], buttons);

        host.Seat.Keyboard.NotifyClearFocus();
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpUntil(() => entered.Count == 2);
        Assert.Equal(0, entered[^1]);
    }

    [Fact]
    public void A_finger_scroll_that_reaches_zero_ends_with_axis_stop()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        var pointer = client.Seat!.GetPointer();
        var log = new List<string>();
        pointer.AxisSourceEvent += (_, e) => log.Add($"source {(uint)e.AxisSource}");
        pointer.AxisRelativeDirectionEvent += (_, e) => log.Add($"direction {(uint)e.Direction}");
        pointer.AxisValue120 += (_, e) => log.Add($"value120 {(uint)e.Axis} {e.Value120}");
        pointer.AxisEvent += (_, e) => log.Add($"axis {(uint)e.Axis} {e.Value.ToDouble():F1}");
        pointer.AxisStop += (_, e) => log.Add($"stop {(uint)e.Axis} {e.Time}");
        host.PumpToClient();

        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 1, 1);
        host.Seat.Pointer.NotifyAxis(10, new PointerAxis(
            WlPointer.Axis.VerticalScroll, 12.5,
            Source: WlPointer.AxisSource.Finger,
            RelativeDirection: WlPointer.AxisRelativeDirection.Inverted));
        host.Seat.Pointer.NotifyAxis(20, new PointerAxis(
            WlPointer.Axis.VerticalScroll, 0, Source: WlPointer.AxisSource.Finger));
        host.Seat.Pointer.NotifyAxis(30, new PointerAxis(WlPointer.Axis.VerticalScroll, 15, 120));
        host.PumpUntil(() => log.Count == 9);

        Assert.Equal(
        [
            "source 1",
            "direction 1",
            "axis 0 12.5",
            "source 1",
            "stop 0 20",
            "source 0",
            "direction 0",
            "value120 0 120",
            "axis 0 15.0",
        ],
            log);
    }

    [Fact]
    public void A_high_resolution_wheel_accumulates_into_whole_steps_for_an_older_client()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        WlSeat? oldSeat = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wl_seat")
            {
                oldSeat = registry.Bind<WlSeat>(e.Name, 7);
            }
        };
        host.PumpToClient();

        var oldPointer = oldSeat!.GetPointer();
        var discrete = new List<int>();
        var values = new List<double>();
#pragma warning disable CS0618
        oldPointer.AxisDiscrete += (_, e) => discrete.Add(e.Discrete);
#pragma warning restore CS0618
        oldPointer.AxisEvent += (_, e) => values.Add(e.Value.ToDouble());
        host.PumpToClient();

        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 1, 1);
        for (var i = 0; i < 3; i++)
        {
            host.Seat.Pointer.NotifyAxis(10, new PointerAxis(WlPointer.Axis.VerticalScroll, 4, 30));
        }

        host.PumpToClient();
        Assert.Empty(discrete);
        Assert.Empty(values);

        host.Seat.Pointer.NotifyAxis(20, new PointerAxis(WlPointer.Axis.VerticalScroll, 4, 30));
        host.PumpUntil(() => discrete.Count == 1);

        Assert.Equal([1], discrete);
        Assert.Equal([16.0], values);
    }

    [Fact]
    public void Set_cursor_honors_the_enter_serial()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        var pointer = client.Seat!.GetPointer();
        uint enterSerial = 0;
        pointer.Enter += (_, e) => enterSerial = e.Serial;
        host.PumpToClient();
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 1, 1);
        host.PumpUntil(() => enterSerial != 0);

        Surface? requested = null;
        var requests = 0;
        host.Seat.Pointer.CursorRequested += request =>
        {
            requested = request.Surface;
            requests++;
        };

        var cursorSurface = client.Compositor.CreateSurface();
        pointer.SetCursor(enterSerial + 99, cursorSurface, 2, 3);
        host.PumpToServer();
        Assert.Equal(0, requests);

        pointer.SetCursor(enterSerial, cursorSurface, 2, 3);
        host.PumpToServer();
        Assert.Equal(1, requests);
        Assert.NotNull(requested);
        Assert.Equal(Basin.Seat.SeatPointer.CursorRole, requested!.Role);
    }
    [Fact]
    public void A_warp_moves_the_pointer_inside_the_focused_surface()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Desktop.PointerWarpManager(host.Display, host.Compositor, host.Seat);

        var client = host.Client;
        var window = MappedToplevel.Map(host, client, 100, 80);
        var pointer = client.Seat!.GetPointer();

        Basin.Desktop.Protocol.WpPointerWarpV1? warp = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_pointer_warp_v1")
            {
                warp = registry.Bind<Basin.Desktop.Protocol.WpPointerWarpV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(warp);

        uint enterSerial = 0;
        var motions = new List<(double X, double Y)>();
        pointer.Enter += (_, e) => enterSerial = e.Serial;
        pointer.Motion += (_, e) => motions.Add((e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble()));

        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 1, 1);
        host.PumpUntil(() => enterSerial != 0);

        var warped = 0;
        manager.Warped += (_, _, _) => warped++;

        warp!.WarpPointer(window.Surface, pointer, WlFixed.FromDouble(40), WlFixed.FromDouble(20), enterSerial);
        host.PumpUntil(() => motions.Count > 0);
        Assert.Equal((40.0, 20.0), motions[^1]);
        Assert.Equal(1, warped);

        warp.WarpPointer(window.Surface, pointer, WlFixed.FromDouble(10), WlFixed.FromDouble(10), enterSerial + 1000);
        warp.WarpPointer(window.Surface, pointer, WlFixed.FromDouble(400), WlFixed.FromDouble(10), enterSerial);
        host.PumpToClient();
        Assert.Equal(1, warped);
        Assert.Equal((40.0, 20.0), motions[^1]);
    }

    [Fact]
    public void A_warp_reaches_version_eleven_as_warp_and_older_pointers_as_motion()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        using var manager = new Basin.Desktop.PointerWarpManager(host.Display, host.Compositor, host.Seat);
        var window = MappedToplevel.Map(host, client, 100, 80);

        var oldPointer = client.Seat!.GetPointer();
        WlSeat? newSeat = null;
        Basin.Desktop.Protocol.WpPointerWarpV1? warp = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wl_seat")
            {
                newSeat = registry.Bind<WlSeat>(e.Name, 11);
            }
            else if (e.Interface == "wp_pointer_warp_v1")
            {
                warp = registry.Bind<Basin.Desktop.Protocol.WpPointerWarpV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var newPointer = newSeat!.GetPointer();

        uint enterSerial = 0;
        var oldMotions = new List<(double X, double Y)>();
        var newMotions = new List<(double X, double Y)>();
        var warps = new List<(double X, double Y)>();
        oldPointer.Enter += (_, e) => enterSerial = e.Serial;
        oldPointer.Motion += (_, e) => oldMotions.Add((e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble()));
        newPointer.Motion += (_, e) => newMotions.Add((e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble()));
        newPointer.Warp += (_, e) => warps.Add((e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble()));

        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 1, 1);
        host.PumpUntil(() => enterSerial != 0);

        warp!.WarpPointer(window.Surface, oldPointer, WlFixed.FromDouble(40), WlFixed.FromDouble(20), enterSerial);
        host.PumpUntil(() => warps.Count == 1 && oldMotions.Count == 1);

        Assert.Equal((40.0, 20.0), warps[0]);
        Assert.Equal((40.0, 20.0), oldMotions[0]);
        Assert.Empty(newMotions);

        host.Seat.Pointer.NotifyMotion(10, 5, 6);
        host.PumpUntil(() => newMotions.Count == 1);
        Assert.Single(warps);
        Assert.Equal((5.0, 6.0), newMotions[0]);
        Assert.Equal(2, oldMotions.Count);
    }
}
