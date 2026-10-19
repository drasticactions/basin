using Basin.Desktop.Protocol;
using Basin.Sdl;
using SDL3;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlPointerLockTests
{
    [Fact]
    public void A_client_lock_puts_the_window_in_relative_mode_and_relative_motion_reaches_the_client()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, scale: 2.0);
        var client = harness.Client;
        var constraints = client.BindAt<ZwpPointerConstraintsV1>("zwp_pointer_constraints_v1", 1);
        var relativeManager = client.BindAt<ZwpRelativePointerManagerV1>("zwp_relative_pointer_manager_v1", 1);
        var pointer = client.Seat!.GetPointer();
        var relative = relativeManager.GetRelativePointer(pointer);
        var deltas = new List<(double Dx, double Dy)>();
        relative.RelativeMotion += (_, e) => deltas.Add((e.Dx.ToDouble(), e.Dy.ToDouble()));
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        _ = xdgSurface.GetToplevel();
        var configured = false;
        xdgSurface.Configure += (_, e) =>
        {
            xdgSurface.AckConfigure(e.Serial);
            configured = true;
        };
        surface.Commit();
        harness.PumpUntil(() => configured, "never configured");
        var buffer = client.CreateBuffer(100, 80, (data, stride) => { });
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        harness.PumpUntil(() => harness.Comp.Shell.Windows.Count == 1, "never mapped");

        var locked = false;
        var lockProxy = constraints.LockPointer(surface, pointer, null, ZwpPointerConstraintsV1.Lifetime.Persistent);
        lockProxy.Locked += (_, _) => locked = true;
        surface.Commit();
        var box = harness.Comp.Shell.Windows[0].ClientBox;
        var window = harness.Comp.Window;
        SdlEvents.Motion(window.Window.Id, (float)((box.X + 10) * 2), (float)((box.Y + 10) * 2));
        harness.PumpUntil(() => locked, "the lock never activated");
        Assert.True(harness.Host.IsPointerLockRequested);
        Assert.True(window.IsRelative);
        Assert.Equal(window.HostRelative, SDL.GetWindowRelativeMouseMode(window.Window.Handle));

        var held = harness.Comp.Shell.PointerPosition;
        SdlEvents.RelativeMotion(window.Window.Id, 8, -4);
        harness.PumpUntil(() => deltas.Count > 0 && deltas[^1] == (4.0, -2.0), "the client never saw the relative motion");
        Assert.Equal(held, harness.Comp.Shell.PointerPosition);

        lockProxy.Dispose();
        harness.PumpUntil(() => !window.IsRelative, "the window never left relative mode");
        Assert.False(window.HostRelative);
        Assert.False(SDL.GetWindowRelativeMouseMode(window.Window.Handle));
    }
}
