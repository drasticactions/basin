using Basin.Desktop;
using Basin.Seat;
using Basin.Seat.Backends;
using Xunit;

namespace Basin.Tests;

public sealed class SeatInjectorTests
{
    [Fact]
    public void Warp_button_key_and_center_stamp_and_ensure_capabilities()
    {
        using var host = new CompositorTestHost(200, 100);
        var pointer = new LayoutPointer(host.Layout);
        var cursor = new CursorController(host.Layout);
        var binder = new SeatBinder(host.Seat, host.Layout, pointer, cursor);
        var moved = 0;
        var buttons = new List<(uint Button, bool Pressed)>();
        var keys = new List<(uint Key, bool Pressed)>();
        var injector = new SeatInjector(binder, host.Seat, host.Layout, pointer)
        {
            Moved = _ => moved++,
            DeliverButton = (_, button, pressed) => buttons.Add((button, pressed)),
            DeliverKey = (_, key, pressed) => keys.Add((key, pressed)),
        };

        injector.Warp(30, 40);
        Assert.Equal(30, pointer.X);
        Assert.Equal(40, pointer.Y);
        Assert.Equal(1, moved);
        Assert.True((host.Seat.Capabilities & SeatCapability.Pointer) != 0);

        injector.Button(Basin.InputCodes.BtnLeft, pressed: true);
        Assert.Equal([(Basin.InputCodes.BtnLeft, true)], buttons);

        injector.Key(30, pressed: true);
        Assert.Equal([(30u, true)], keys);
        Assert.True((host.Seat.Capabilities & SeatCapability.Keyboard) != 0);

        injector.Center();
        Assert.Equal(100, pointer.X);
        Assert.Equal(50, pointer.Y);
        Assert.Equal(2, moved);
        cursor.Dispose();
    }

    [Fact]
    public void Route_sends_injected_input_through_the_injector()
    {
        using var host = new CompositorTestHost(200, 100);
        var pointer = new LayoutPointer(host.Layout);
        var cursor = new CursorController(host.Layout);
        var binder = new SeatBinder(host.Seat, host.Layout, pointer, cursor);
        var buttons = new List<(uint Button, bool Pressed)>();
        var keys = new List<(uint Key, bool Pressed)>();
        var axes = new List<(uint Axis, double Value, uint Source)>();
        var injector = new SeatInjector(binder, host.Seat, host.Layout, pointer)
        {
            DeliverButton = (_, button, pressed) => buttons.Add((button, pressed)),
            DeliverKey = (_, key, pressed) => keys.Add((key, pressed)),
            DeliverAxis = (_, axis, value, source) => axes.Add((axis, value, source)),
        };
        var sink = new HookInputSink();
        sink.Route(injector, host.Seat);

        Assert.True(sink.PointerMotionAbsolute(1, 50, 25, 100, 100));
        Assert.Equal((100.0, 25.0), (pointer.X, pointer.Y));
        Assert.True(sink.PointerMotion(2, 10, 5));
        Assert.Equal((110.0, 30.0), (pointer.X, pointer.Y));
        Assert.True(sink.PointerMotionAbsolute(3, 10, 10, 0, 100));
        Assert.Equal((110.0, 30.0), (pointer.X, pointer.Y));

        Assert.True(sink.PointerButton(4, Basin.InputCodes.BtnLeft, true));
        Assert.Equal([(Basin.InputCodes.BtnLeft, true)], buttons);
        Assert.True(sink.Key(null, 5, 30, true));
        Assert.Equal([(30u, true)], keys);

        Assert.True(sink.PointerAxisSource(1));
        Assert.True(sink.PointerAxis(6, 0, 15));
        Assert.Equal([(0u, 15.0, 1u)], axes);

        var routedKeys = new List<uint>();
        sink.Route(injector, host.Seat, (_, _, key, _) => routedKeys.Add(key));
        Assert.True(sink.Key(null, 7, 31, true));
        Assert.Equal([31u], routedKeys);
        Assert.Equal([(30u, true)], keys);

        injector.DeliverAxis = null;
        sink.Route(injector, host.Seat);
        Assert.Null(sink.OnPointerAxis);
        cursor.Dispose();
    }

    [Fact]
    public void Touch_reaches_the_touch_driver_and_is_refused_without_one()
    {
        using var host = new CompositorTestHost(200, 100);
        var pointer = new LayoutPointer(host.Layout);
        var cursor = new CursorController(host.Layout);
        var binder = new SeatBinder(host.Seat, host.Layout, pointer, cursor);
        var injector = new SeatInjector(binder, host.Seat, host.Layout, pointer);

        Assert.False(injector.TouchDown(1, 0, 10, 10));

        var downs = new List<(int Id, double X, double Y)>();
        binder.TouchDown += (_, id, x, y) => downs.Add((id, x, y));
        var ups = 0;
        binder.TouchUp += (_, _) => ups++;
        binder.TouchFrame += () => { };

        Assert.True(injector.TouchDown(2, 3, 40, 50));
        Assert.True(injector.TouchFrame());
        Assert.True(injector.TouchUp(3, 3));
        Assert.Equal([(3, 40.0, 50.0)], downs);
        Assert.Equal(1, ups);
        Assert.True((host.Seat.Capabilities & SeatCapability.Touch) != 0);
        cursor.Dispose();
    }

    [Fact]
    public void MotionAbsolute_maps_the_extent_onto_the_layout_bounds()
    {
        using var host = new CompositorTestHost(200, 100);
        var pointer = new LayoutPointer(host.Layout);
        var cursor = new CursorController(host.Layout);
        var binder = new SeatBinder(host.Seat, host.Layout, pointer, cursor);
        (uint Time, double Dx, double Dy)? movedBy = null;
        var injector = new SeatInjector(binder, host.Seat, host.Layout, pointer)
        {
            MovedBy = (timeMs, dx, dy) => movedBy = (timeMs, dx, dy),
        };

        Assert.True(injector.MotionAbsolute(7, 0.5, 0.5, 1.0, 1.0));
        Assert.Equal(100, pointer.X);
        Assert.Equal(50, pointer.Y);
        Assert.NotNull(movedBy);
        Assert.Equal(7u, movedBy!.Value.Time);
        cursor.Dispose();
    }

    [Fact]
    public void A_binder_injects_through_its_own_events_and_becomes_the_seat_default()
    {
        using var host = new CompositorTestHost(200, 100);
        var pointer = new LayoutPointer(host.Layout);
        var binder = new SeatBinder(host.Seat, host.Layout, pointer, new CursorController(host.Layout));
        var second = new SeatBinder(host.Seat, host.Layout, new LayoutPointer(host.Layout), new CursorController(host.Layout));
        Assert.Same(binder.Injector, host.Seat.SyntheticInput);
        Assert.NotSame(second.Injector, host.Seat.SyntheticInput);

        var events = new List<string>();
        binder.Motion += (_, dx, dy, _, _) => events.Add(FormattableString.Invariant($"motion {dx} {dy}"));
        binder.Button += (_, button, pressed) => events.Add($"button {button} {pressed}");
        binder.Key += (_, key, pressed) => events.Add($"key {key} {pressed}");
        binder.Axis += (_, axis) => events.Add(FormattableString.Invariant($"axis {axis.Value}"));
        var input = binder.Injector;
        Assert.True(input.PointerMotionAbsolute(1, 30, 40));
        Assert.True(input.PointerButton(2, Basin.InputCodes.BtnLeft, true));
        Assert.True(input.Key(3, 30, true));
        Assert.True(input.PointerAxis(4, 0, 15, 0));
        Assert.Equal(["motion 30 40", $"button {Basin.InputCodes.BtnLeft} True", "key 30 True", "axis 15"], events);
        Assert.Equal((30.0, 40.0), (pointer.X, pointer.Y));
    }

    [Fact]
    public void The_seat_default_forwards_and_refuses_while_the_session_is_locked()
    {
        using var host = new CompositorTestHost(200, 100);
        var services = new BasinServices(host.Loop);
        var lockState = new SwitchLock();
        services.Use<Basin.Capabilities.ILockState>(lockState);
        var proxy = new SeatSyntheticInput(host.Seat, services);
        Assert.False(proxy.IsAvailable);
        Assert.False(proxy.Key(1, 30, true));

        var binder = new SeatBinder(host.Seat, host.Layout, new LayoutPointer(host.Layout), new CursorController(host.Layout));
        var keys = 0;
        binder.Key += (_, _, _) => keys++;
        Assert.True(proxy.Key(1, 30, true));
        lockState.IsLocked = true;
        Assert.False(proxy.Key(2, 30, false));
        Assert.False(proxy.PointerButton(3, Basin.InputCodes.BtnLeft, true));
        proxy.AllowWhileLocked = true;
        Assert.True(proxy.Key(4, 30, false));
        Assert.Equal(2, keys);
    }

    internal sealed class SwitchLock : Basin.Capabilities.ILockState
    {
        public bool IsLocked { get; set; }

        public void AddObserver(Basin.Capabilities.ILockStateObserver observer)
        {
        }

        public void RemoveObserver(Basin.Capabilities.ILockStateObserver observer)
        {
        }
    }
}
