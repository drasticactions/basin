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
}
