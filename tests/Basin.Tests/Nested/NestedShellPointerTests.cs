using Basin.Desktop.Protocol;
using Basin.Diagnostics;
using Basin.Hosted;
using Xunit;

namespace Basin.Tests.Nested;

public sealed class NestedShellPointerTests
{
    private static double Fixed(Wayland.WlFixed value) => value.ToDouble();

    [Fact]
    public void A_pointer_lock_holds_the_cursor_sends_relative_motion_and_asks_the_host_to_lock()
    {
        using var harness = new NestedShellHarness();
        var client = harness.Client;
        var constraints = client.BindAt<ZwpPointerConstraintsV1>("zwp_pointer_constraints_v1", 1);
        var relativeManager = client.BindAt<ZwpRelativePointerManagerV1>("zwp_relative_pointer_manager_v1", 1);
        var pointer = client.Seat!.GetPointer();
        var relative = relativeManager.GetRelativePointer(pointer);
        var deltas = new List<(double Dx, double Dy, double RawDx)>();
        relative.RelativeMotion += (_, e) => deltas.Add((Fixed(e.Dx), Fixed(e.Dy), Fixed(e.DxUnaccel)));
        var toplevel = harness.MapToplevel(200, 150);
        var box = harness.Shell.Windows[0].ClientBox;

        var requests = new List<bool>();
        var warps = new List<(double X, double Y)>();
        harness.Host.PointerLockRequested += requests.Add;
        harness.Host.PointerWarpRequested += (x, y) => warps.Add((x, y));

        var locked = false;
        var unlocked = false;
        var lockProxy = constraints.LockPointer(toplevel.Surface, pointer, null, ZwpPointerConstraintsV1.Lifetime.Persistent);
        lockProxy.Locked += (_, _) => locked = true;
        lockProxy.Unlocked += (_, _) => unlocked = true;
        lockProxy.SetCursorPositionHint(Wayland.WlFixed.FromDouble(40), Wayland.WlFixed.FromDouble(30));
        toplevel.Surface.Commit();
        harness.Pump();

        NestedShellHarness.Move(harness.Shell, box.X + 20, box.Y + 20);
        harness.PumpUntil(() => locked, "the lock never activated");
        Assert.Equal([true], requests);
        Assert.True(harness.Shell.PointerLocked);
        var held = harness.Shell.PointerPosition;

        harness.Shell.HandleInput(new BasinViewInput(BasinViewInputKind.PointerRelative, 10, 0.5, -0.25, 0, false, 3.0, -1.5, 0));
        harness.PumpUntil(() => deltas.Count > 0, "the client never saw relative motion");
        Assert.Equal((3.0, -1.5, 0.5), deltas[^1]);
        Assert.Equal(held, harness.Shell.PointerPosition);

        NestedShellHarness.Move(harness.Shell, box.X + 90, box.Y + 70);
        harness.Pump();
        Assert.Equal(held, harness.Shell.PointerPosition);

        lockProxy.Dispose();
        harness.PumpUntil(() => requests.Count == 2, "the host was never asked to unlock");
        Assert.Equal([true, false], requests);
        Assert.Equal([(box.X + 40.0, box.Y + 30.0)], warps);
        Assert.False(unlocked && harness.Shell.PointerLocked);
    }

    [Fact]
    public void Ordinary_motion_also_reaches_relative_pointer_clients()
    {
        using var harness = new NestedShellHarness();
        var client = harness.Client;
        var relativeManager = client.BindAt<ZwpRelativePointerManagerV1>("zwp_relative_pointer_manager_v1", 1);
        var pointer = client.Seat!.GetPointer();
        var relative = relativeManager.GetRelativePointer(pointer);
        var deltas = new List<(double Dx, double Dy)>();
        relative.RelativeMotion += (_, e) => deltas.Add((Fixed(e.Dx), Fixed(e.Dy)));
        harness.MapToplevel(200, 150);
        var box = harness.Shell.Windows[0].ClientBox;

        NestedShellHarness.Move(harness.Shell, box.X + 20, box.Y + 20);
        NestedShellHarness.Move(harness.Shell, box.X + 25, box.Y + 18);
        harness.PumpUntil(() => deltas.Count > 0 && deltas[^1] == (5.0, -2.0), "the client never saw the delta");
    }
}
