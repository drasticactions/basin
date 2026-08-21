using Basin.Backend.Libinput;
using Xunit;

namespace Basin.Tests;

public sealed class InputBridgeTests
{
    [Fact]
    public void Physical_positions_land_in_logical_layout_coordinates()
    {
        using var host = new CompositorTestHost();
        var left = host.Backend.CreateOutput(new OutputMode(800, 600, 60_000));
        var right = host.Backend.CreateOutput(new OutputMode(800, 600, 60_000));
        using (var state = new OutputState())
        {
            right.Commit(state.SetScale(2));
        }

        var layout = new OutputLayout();
        layout.Add(left, 0, 0);
        layout.Add(right, 800, 0);

        Assert.Equal((100.0, 50.0), layout.ToLayout(left, 100, 50));

        Assert.Equal((850.0, 25.0), layout.ToLayout(right, 100, 50));

        Assert.Equal((400.0, 300.0), layout.FromNormalized(left, 0.5, 0.5));
        Assert.Equal((1000.0, 150.0), layout.FromNormalized(right, 0.5, 0.5));

        left.Destroy();
        right.Destroy();
    }

    [Fact]
    public void Motion_at_a_hit_enters_and_moves_and_a_miss_clears_focus()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        host.Seat.Pointer.NotifyMotionAt(1, window.ServerSurface, 12, 34, 12, 34);
        Assert.Equal(window.ServerSurface, host.Seat.Pointer.Focus);
        Assert.Equal(12, host.Seat.Pointer.X);
        Assert.Equal(34, host.Seat.Pointer.Y);

        host.Seat.Pointer.NotifyMotionAt(2, null, 0, 0, 900, 700);
        Assert.Null(host.Seat.Pointer.Focus);
    }

    [Fact]
    public void A_held_button_pins_pointer_focus_until_release()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        host.Seat.Pointer.NotifyMotionAt(1, window.ServerSurface, 40, 30, 140, 130);
        host.Seat.Pointer.NotifyButton(2, 0x110, pressed: true);

        host.Seat.Pointer.NotifyMotionAt(3, null, 0, 0, 190, 150);
        Assert.Equal(window.ServerSurface, host.Seat.Pointer.Focus);
        Assert.Equal(90, host.Seat.Pointer.X);
        Assert.Equal(50, host.Seat.Pointer.Y);

        host.Seat.Pointer.NotifyButton(4, 0x110, pressed: false);
        host.Seat.Pointer.NotifyMotionAt(5, null, 0, 0, 190, 150);
        Assert.Null(host.Seat.Pointer.Focus);
    }

    [Fact]
    public void Routing_motion_allocates_nothing()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        for (var i = 0; i < 20; i++)
        {
            host.Seat.Pointer.NotifyMotionAt((uint)i, window.ServerSurface, i, i, i, i);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            host.Seat.Pointer.NotifyMotionAt((uint)i, window.ServerSurface, i % 64, i % 48, i % 64, i % 48);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Buttons_and_keys_translate_from_the_bool_backends_report()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Pointer.NotifyMotionAt(1, window.ServerSurface, 1, 1, 1, 1);

        host.Seat.Pointer.NotifyButton(2, 0x110, pressed: true);
        Assert.True(host.Seat.Pointer.HasImplicitGrab);

        host.Seat.Pointer.NotifyButton(3, 0x110, pressed: false);
        Assert.False(host.Seat.Pointer.HasImplicitGrab);

        host.Seat.Keyboard.SetKeymap();
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.Seat.Keyboard.NotifyKey(4, 30, pressed: true);
        Assert.Contains(30u, host.Seat.Keyboard.PressedKeys);
        host.Seat.Keyboard.NotifyKey(5, 30, pressed: false);
        Assert.DoesNotContain(30u, host.Seat.Keyboard.PressedKeys);
    }

    [Fact]
    public void Libinput_scroll_vocabulary_maps_onto_the_protocol()
    {
        Assert.Equal(
            Wayland.WlPointer.Axis.VerticalScroll,
            ScrollOrientation.Vertical.ToAxis());
        Assert.Equal(
            Wayland.WlPointer.Axis.HorizontalScroll,
            ScrollOrientation.Horizontal.ToAxis());

        Assert.Equal(
            Wayland.WlPointer.AxisSource.Finger,
            ScrollSource.Finger.ToAxisSource());
        Assert.Equal(
            Wayland.WlPointer.AxisSource.Continuous,
            ScrollSource.Continuous.ToAxisSource());
        Assert.Equal(
            Wayland.WlPointer.AxisSource.Wheel,
            ScrollSource.Wheel.ToAxisSource());
    }
}
