using System.Runtime.InteropServices;
using Basin.Hypr.InputCapture;
using Libei;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class InputCaptureBarrierTests
{
    [Fact]
    public void Wire_coordinates_are_two_s_complement()
    {
        var barrier = InputCaptureBarriers.FromWire(7, unchecked((uint)-1920), 0, unchecked((uint)-1920), 1079);
        Assert.Equal(-1920, barrier.X1);
        Assert.Equal(-1920, barrier.X2);
        Assert.Equal(1079, barrier.Y2);
    }

    [Fact]
    public void A_barrier_must_span_one_output_edge_corner_to_corner()
    {
        var layout = new OutputLayout();
        var left = new TestPreferenceOutput("left");
        var right = new TestPreferenceOutput("right");
        using (var mode = new OutputState())
        {
            Assert.True(left.Commit(mode.SetMode(new OutputMode(1920, 1080, 60_000))));
        }

        using (var mode = new OutputState())
        {
            Assert.True(right.Commit(mode.SetMode(new OutputMode(1280, 720, 60_000))));
        }

        layout.Add(left, -1920, 0);
        layout.Add(right, 0, 0);

        Assert.True(InputCaptureBarriers.IsValid(new InputCaptureBarrier(1, -1920, 0, -1920, 1079), layout));
        Assert.True(InputCaptureBarriers.IsValid(new InputCaptureBarrier(2, 1280, 0, 1280, 719), layout));
        Assert.True(InputCaptureBarriers.IsValid(new InputCaptureBarrier(3, -1920, 0, -1, 0), layout));
        Assert.True(InputCaptureBarriers.IsValid(new InputCaptureBarrier(4, 0, 720, 1279, 720), layout));
        Assert.False(InputCaptureBarriers.IsValid(new InputCaptureBarrier(5, -1920, 10, -1920, 500), layout));
        Assert.False(InputCaptureBarriers.IsValid(new InputCaptureBarrier(6, 5, 5, 40, 40), layout));
        Assert.False(InputCaptureBarriers.IsValid(new InputCaptureBarrier(7, 3, 3, 3, 3), layout));
        Assert.False(InputCaptureBarriers.IsValid(new InputCaptureBarrier(8, -1920, 0, 1279, 0), layout));
    }

    [Fact]
    public void A_motion_segment_crosses_a_barrier_it_passes_through()
    {
        var barrier = new InputCaptureBarrier(1, 0, 0, 0, 119);
        Assert.True(InputCaptureBarriers.Crosses(barrier, 5, 50, -3, 50));
        Assert.True(InputCaptureBarriers.Crosses(barrier, 0.5, 50, -0.5, 60));
        Assert.False(InputCaptureBarriers.Crosses(barrier, 5, 50, 1, 50));
        Assert.False(InputCaptureBarriers.Crosses(barrier, 5, 130, -3, 130));
        Assert.False(InputCaptureBarriers.Crosses(barrier, 0, 10, 0, 20));
    }
}

public sealed class HyprlandInputCaptureTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    private static (Basin.Hypr.InputCapture.Protocol.HyprlandInputCaptureV1 Session, int Fd) CreateSession(
        CompositorTestHost host, string handle = "session-1")
    {
        var manager = HyprlandTestSupport.Bind<Basin.Hypr.InputCapture.Protocol.HyprlandInputCaptureManagerV1>(
            host, "hyprland_input_capture_manager_v1", 1);
        var session = manager.CreateSession(handle);
        var fd = -1;
        session.EisFd += (_, e) => fd = e.Fd;
        host.PumpUntil(() => fd >= 0);
        Assert.True(fd >= 0);
        return (session, fd);
    }

    [Fact]
    public void A_session_gets_an_eis_fd_and_a_barrier_crossing_captures_the_pointer()
    {
        Assert.SkipUnless(InputCaptureLibrary.IsAvailable(out var whyNot), whyNot ?? "libeis");
        using var host = new CompositorTestHost();
        using var manager = new HyprlandInputCaptureManager(host.Display, host.Loop, host.Layout, host.Seat);
        var (session, fd) = CreateSession(host);
        _ = close(fd);

        var activated = new List<(uint Id, double X, double Y, uint Barrier)>();
        var deactivated = new List<uint>();
        session.Activated += (_, e) => activated.Add((e.ActivationId, e.X.ToDouble(), e.Y.ToDouble(), e.BarrierId));
        session.Deactivated += (_, e) => deactivated.Add(e.ActivationId);
        session.AddBarrier(1, 9, 0, 0, 0, 119);
        session.Enable();
        host.PumpToServer();
        Assert.Equal(1, manager.SessionCount);

        Assert.False(manager.NotifyMotion(1, 5, 50, -2, 0));
        Assert.False(manager.IsCaptured);

        Assert.True(manager.NotifyMotion(2, -3, 50, -8, 0));
        Assert.True(manager.IsCaptured);
        Assert.True(host.Seat.Pointer.HasGrab);
        Assert.True(host.Seat.Keyboard.HasGrab);
        host.PumpUntil(() => activated.Count == 1);
        Assert.Equal((1u, -3.0, 50.0, 9u), activated[0]);

        Assert.True(manager.NotifyMotion(3, -10, 50, -7, 0));

        var warps = new List<(double X, double Y)>();
        manager.WarpRequested += (x, y) => warps.Add((x, y));
        session.Release(1, WlFixed.FromDouble(40), WlFixed.FromDouble(30));
        host.PumpUntil(() => deactivated.Count == 1);
        Assert.Equal([1u], deactivated);
        Assert.Equal([(40.0, 30.0)], warps);
        Assert.False(manager.IsCaptured);
        Assert.False(host.Seat.Pointer.HasGrab);
        Assert.False(manager.NotifyMotion(4, 20, 50, 1, 0));
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void Input_arrives_over_eis_while_captured()
    {
        Assert.SkipUnless(InputCaptureLibrary.IsAvailable(out var whyNot), whyNot ?? "libeis");
        using var host = new CompositorTestHost();
        using var manager = new HyprlandInputCaptureManager(host.Display, host.Loop, host.Layout, host.Seat);
        var (session, fd) = CreateSession(host);

        using var ei = EiContext.CreateReceiver("basin-test");
        ei.ConnectToFd(fd);
        var motions = new List<(double Dx, double Dy)>();
        var keys = new List<(uint Key, bool Pressed)>();
        var emulating = new List<uint>();
        var devices = 0;

        void PumpEi()
        {
            for (var round = 0; round < 10; round++)
            {
                host.Loop.Dispatch(0);
                host.Display.FlushClients();
                ei.Dispatch();
                while (ei.TryGetEvent(out var @event))
                {
                    using (@event)
                    {
                        switch (@event.Type)
                        {
                            case EiEventType.SeatAdded:
                                using (var seat = @event.GetSeat())
                                {
                                    seat!.BindCapabilities(
                                        EiDeviceCapability.Pointer | EiDeviceCapability.Button |
                                        EiDeviceCapability.Scroll | EiDeviceCapability.Keyboard);
                                }

                                break;

                            case EiEventType.DeviceAdded:
                                devices++;
                                break;

                            case EiEventType.DeviceStartEmulating:
                                emulating.Add(((EiEmulatingEvent)@event).Sequence);
                                break;

                            case EiEventType.PointerMotion:
                                var motion = (EiPointerMotionEvent)@event;
                                motions.Add((motion.Dx, motion.Dy));
                                break;

                            case EiEventType.KeyboardKey:
                                var key = (EiKeyboardKeyEvent)@event;
                                keys.Add((key.Key, key.IsPress));
                                break;
                        }
                    }
                }
            }
        }

        PumpEi();
        Assert.Equal(2, devices);

        session.AddBarrier(1, 3, 0, 0, 0, 119);
        session.Enable();
        host.PumpToServer();
        Assert.True(manager.NotifyMotion(1, -3, 50, -8, 0));
        Assert.True(manager.NotifyMotion(2, -5, 52, -2, 2));
        host.Seat.Pointer.NotifyButton(3, InputCodes.BtnLeft, true);
        host.Seat.Keyboard.NotifyKey(4, 30, true);
        host.Seat.Keyboard.NotifyKey(5, 30, false);
        PumpEi();

        Assert.Equal([1u, 1u], emulating);
        Assert.Equal([(-8.0, 0.0), (-2.0, 2.0)], motions);
        Assert.Equal([(30u, true), (30u, false)], keys);

        session.Release(1, WlFixed.FromDouble(-1), WlFixed.FromDouble(-1));
        host.PumpToServer();
        Assert.False(manager.IsCaptured);
        ei.Disconnect();
        PumpEi();
        host.PumpToServer();
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void A_barrier_off_the_edge_is_invalid_barrier()
    {
        Assert.SkipUnless(InputCaptureLibrary.IsAvailable(out var whyNot), whyNot ?? "libeis");
        using var host = new CompositorTestHost();
        using var manager = new HyprlandInputCaptureManager(host.Display, host.Loop, host.Layout, host.Seat);
        var (session, fd) = CreateSession(host);
        _ = close(fd);

        session.AddBarrier(1, 1, 10, 10, 10, 50);
        HyprlandTestSupport.AssertKilled(host);
    }

    [Fact]
    public void A_duplicate_barrier_id_is_invalid_barrier_id()
    {
        Assert.SkipUnless(InputCaptureLibrary.IsAvailable(out var whyNot), whyNot ?? "libeis");
        using var host = new CompositorTestHost();
        using var manager = new HyprlandInputCaptureManager(host.Display, host.Loop, host.Layout, host.Seat);
        var (session, fd) = CreateSession(host);
        _ = close(fd);

        session.AddBarrier(1, 1, 0, 0, 0, 119);
        session.AddBarrier(1, 1, 160, 0, 160, 119);
        HyprlandTestSupport.AssertKilled(host);
    }

    [Fact]
    public void Releasing_with_the_wrong_activation_id_is_invalid_activation_id()
    {
        Assert.SkipUnless(InputCaptureLibrary.IsAvailable(out var whyNot), whyNot ?? "libeis");
        using var host = new CompositorTestHost();
        using var manager = new HyprlandInputCaptureManager(host.Display, host.Loop, host.Layout, host.Seat);
        var (session, fd) = CreateSession(host);
        _ = close(fd);

        session.AddBarrier(1, 1, 0, 0, 0, 119);
        session.Enable();
        host.PumpToServer();
        Assert.True(manager.NotifyMotion(1, -3, 50, -8, 0));

        session.Release(99, WlFixed.FromDouble(-1), WlFixed.FromDouble(-1));
        HyprlandTestSupport.AssertKilled(host);
    }

    [Fact]
    public void A_layout_change_clears_barriers_and_disables_the_session()
    {
        Assert.SkipUnless(InputCaptureLibrary.IsAvailable(out var whyNot), whyNot ?? "libeis");
        using var host = new CompositorTestHost();
        using var manager = new HyprlandInputCaptureManager(host.Display, host.Loop, host.Layout, host.Seat);
        var (session, fd) = CreateSession(host);
        _ = close(fd);

        var disabled = 0;
        session.Disabled += (_, _) => disabled++;
        session.AddBarrier(1, 1, 0, 0, 0, 119);
        session.Enable();
        host.PumpToServer();

        host.Layout.Move(host.Output, 10, 0);
        host.PumpUntil(() => disabled == 1);
        Assert.False(manager.NotifyMotion(1, -3, 50, -8, 0));
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void The_module_needs_a_seat_and_installs_where_libeis_loads()
    {
        using var host = new CompositorTestHost();
        using var without = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Install(new HyprlandInputCaptureModule());
        var error = Assert.Throws<InvalidOperationException>(() => without.Freeze());
        Assert.Contains("Without(\"hyprland_input_capture_manager_v1\")", error.Message, StringComparison.Ordinal);

        using var with = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Use(host.Seat)
            .Install(InputCapturePack.Default)
            .Freeze();
        Assert.Equal(
            InputCaptureLibrary.IsAvailable(out _),
            with.Modules.ContainsKey("hyprland_input_capture_manager_v1"));
    }
}
