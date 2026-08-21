using Xunit;

namespace Basin.Tests;

public class NestedGestureTests
{
    private static readonly NestedParentOptions Gesturing =
        NestedParentOptions.Undecorating with { PointerGestures = true };

    [Fact]
    public void Parent_swipes_reach_the_nested_pointer()
    {
        using var host = new NestedBackendTestHost(Gesturing);
        var output = host.CreateOutput();

        var begins = new List<(uint Time, uint Fingers)>();
        var updates = new List<(uint Time, double Dx, double Dy)>();
        var ends = new List<(uint Time, bool Cancelled)>();

        var pointer = host.Backend.Pointer;
        Assert.NotNull(pointer);
        pointer!.SwipeBegin += (time, fingers) => begins.Add((time, fingers));
        pointer.SwipeUpdate += (time, dx, dy) => updates.Add((time, dx, dy));
        pointer.SwipeEnd += (time, cancelled) => ends.Add((time, cancelled));

        FocusParentPointer(host);
        host.Parent.Invoke(() =>
        {
            host.Parent.Gestures!.NotifySwipeBegin(10, 3);
            host.Parent.Gestures!.NotifySwipeUpdate(20, -12.5, 2.25);
            host.Parent.Gestures!.NotifySwipeEnd(30);
        });
        host.Pump();

        Assert.Equal((10u, 3u), Assert.Single(begins));
        Assert.Equal((20u, -12.5, 2.25), Assert.Single(updates));
        Assert.Equal((30u, false), Assert.Single(ends));
        Assert.NotNull(output);
    }

    [Fact]
    public void A_cancelled_swipe_arrives_as_cancelled()
    {
        using var host = new NestedBackendTestHost(Gesturing);
        _ = host.CreateOutput();

        var ends = new List<bool>();
        host.Backend.Pointer!.SwipeEnd += (_, cancelled) => ends.Add(cancelled);

        FocusParentPointer(host);
        host.Parent.Invoke(() =>
        {
            host.Parent.Gestures!.NotifySwipeBegin(10, 3);
            host.Parent.Gestures!.NotifySwipeEnd(20, canceled: true);
        });
        host.Pump();

        Assert.True(Assert.Single(ends));
    }

    [Fact]
    public void Pinch_and_hold_reach_the_nested_pointer_too()
    {
        using var host = new NestedBackendTestHost(Gesturing);
        _ = host.CreateOutput();

        var pinches = new List<(uint Fingers, double Scale, double Rotation)>();
        var holds = new List<uint>();
        var pointer = host.Backend.Pointer!;
        pointer.PinchUpdate += (_, _, _, scale, rotation) => pinches.Add((0, scale, rotation));
        pointer.HoldBegin += (_, fingers) => holds.Add(fingers);

        FocusParentPointer(host);
        host.Parent.Invoke(() =>
        {
            host.Parent.Gestures!.NotifyPinchBegin(10, 2);
            host.Parent.Gestures!.NotifyPinchUpdate(20, 0, 0, 1.5, 30);
            host.Parent.Gestures!.NotifyPinchEnd(30);
            host.Parent.Gestures!.NotifyHoldBegin(40, 3);
            host.Parent.Gestures!.NotifyHoldEnd(50);
        });
        host.Pump();

        var pinch = Assert.Single(pinches);
        Assert.Equal(1.5, pinch.Scale, 3);
        Assert.Equal(30, pinch.Rotation, 3);
        Assert.Equal(3u, Assert.Single(holds));
    }

    [Fact]
    public void A_parent_without_the_global_leaves_the_binding_null()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        _ = host.CreateOutput();

        var begins = 0;
        host.Backend.Pointer!.SwipeBegin += (_, _) => begins++;
        host.Pump();

        Assert.Equal(0, begins);
    }

    private static void FocusParentPointer(NestedBackendTestHost host)
    {
        host.Pump();
        host.Parent.Invoke(() =>
        {
            var toplevel = host.Parent.Toplevels[0];
            host.Parent.Seat!.Pointer.NotifyEnter(toplevel.Surface, 5, 5);
        });
        host.Pump();
    }
}
