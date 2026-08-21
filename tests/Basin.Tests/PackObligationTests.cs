using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class PackObligationTests
{
    public static TheoryData<string> DriveShaped =>
    [
        "wp_fifo_manager_v1",
        "wp_commit_timing_manager_v1",
    ];

    public static TheoryData<string, Type> PolicyShaped => new()
    {
        { "xdg_activation_v1", typeof(IActivationTokens) },
        { "xdg_system_bell_v1", typeof(IBell) },
    };

    [Theory]
    [MemberData(nameof(PolicyShaped))]
    public void A_policy_shaped_module_without_its_capability_refuses_to_freeze(string wireInterface, Type capability)
    {
        using var host = new CompositorTestHost();
        using var services = Registry(host).Install(ModuleFor(wireInterface));

        var error = Assert.Throws<InvalidOperationException>(() => services.Freeze());

        Assert.Contains(wireInterface, error.Message, StringComparison.Ordinal);
        Assert.Contains(capability.Name, error.Message, StringComparison.Ordinal);
        Assert.Contains($"Without(\"{wireInterface}\")", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PolicyShaped))]
    public void Subtracting_a_policy_shaped_module_removes_the_obligation_and_the_global(
        string wireInterface, Type capability)
    {
        using var host = new CompositorTestHost();
        using var services = Registry(host)
            .Without(wireInterface)
            .Install(ModuleFor(wireInterface))
            .Freeze();

        Assert.False(services.Modules.ContainsKey(wireInterface));
        Assert.Null(services.Find<IBell>());
        Assert.DoesNotContain(capability, services.UnresolvedCapabilities);
    }

    [Theory]
    [MemberData(nameof(DriveShaped))]
    public void A_drive_shaped_module_without_a_frame_clock_refuses_to_freeze(string wireInterface)
    {
        using var host = new CompositorTestHost();
        using var services = Registry(host).Install(ModuleFor(wireInterface));

        var error = Assert.Throws<InvalidOperationException>(() => services.Freeze());

        Assert.Contains(wireInterface, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IFrameClock), error.Message, StringComparison.Ordinal);
        Assert.Contains($"Without(\"{wireInterface}\")", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DriveShaped))]
    public void Subtracting_a_drive_shaped_module_removes_the_obligation_and_the_global(string wireInterface)
    {
        using var host = new CompositorTestHost();
        using var services = Registry(host)
            .Without(wireInterface)
            .Install(ModuleFor(wireInterface))
            .Freeze();

        Assert.False(services.Modules.ContainsKey(wireInterface));
    }

    [Theory]
    [MemberData(nameof(DriveShaped))]
    public void A_registered_frame_clock_satisfies_the_obligation(string wireInterface)
    {
        using var host = new CompositorTestHost();
        using var services = Registry(host)
            .Use<IFrameClock>(new FrameClock())
            .Install(ModuleFor(wireInterface))
            .Freeze();

        Assert.True(services.Modules.ContainsKey(wireInterface));
    }

    [Fact]
    public void The_clock_alone_retires_a_fifo_barrier_for_a_real_client()
    {
        using var host = new CompositorTestHost();
        var frames = new FrameClock();
        using var services = Registry(host)
            .Use<IFrameClock>(frames)
            .Install(new FifoModule())
            .Freeze();

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;
        var fifo = Bind<Basin.Desktop.Protocol.WpFifoManagerV1>(host, "wp_fifo_manager_v1", 1).GetFifo(surface);

        FifoTests.Paint(client, surface, 10);
        fifo.SetBarrier();
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 10);

        FifoTests.Paint(client, surface, 20);
        fifo.WaitBarrier();
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(10, server!.Current.Width);
        Assert.True(server.HasParkedCommits);

        frames.BeginFrame(host.Output, MonotonicClock.Nanos);
        Assert.Equal(10, server.Current.Width);

        frames.BeginFrame(host.Output, MonotonicClock.Nanos);
        Assert.Equal(20, server.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    [Fact]
    public void The_clock_alone_lands_a_commit_timing_target_on_the_frame_that_presents_it()
    {
        using var host = new CompositorTestHost();
        var frames = new FrameClock();
        using var services = Registry(host)
            .Use<IFrameClock>(frames)
            .Install(new CommitTimingModule())
            .Freeze();

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;

        FifoTests.Paint(client, surface, 10);
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 10);

        var target = MonotonicClock.Nanos + 200_000_000;
        var timer = Bind<Basin.Desktop.Protocol.WpCommitTimingManagerV1>(host, "wp_commit_timing_manager_v1", 1)
            .GetTimer(surface);
        var seconds = (ulong)(target / 1_000_000_000);
        timer.SetTimestamp((uint)(seconds >> 32), (uint)seconds, (uint)(target % 1_000_000_000));
        FifoTests.Paint(client, surface, 20);
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(10, server!.Current.Width);
        Assert.True(server.HasParkedCommits);

        frames.BeginFrame(host.Output, target - 16_000_000);
        Assert.Equal(10, server.Current.Width);

        frames.BeginFrame(host.Output, target);
        Assert.Equal(20, server.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    [Fact]
    public void The_whole_desktop_pack_has_every_obligation_a_host_registry_meets()
    {
        using var host = new CompositorTestHost();
        using var services = Registry(host)
            .Use<IFrameClock>(new FrameClock())
            .Use<IActivationTokens>(new DefaultActivationTokens())
            .Use<IBell>(SilentBell.Instance)
            .Install(DesktopPack.Desktop)
            .Freeze();

        Assert.True(services.Modules.ContainsKey("wp_fifo_manager_v1"));
        Assert.True(services.Modules.ContainsKey("wp_commit_timing_manager_v1"));
        Assert.True(services.Modules.ContainsKey("xdg_activation_v1"));
        Assert.True(services.Modules.ContainsKey("xdg_system_bell_v1"));
    }

    private static BasinServices Registry(CompositorTestHost host) =>
        new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Use(host.Seat);

    private static IProtocolModule ModuleFor(string wireInterface) => wireInterface switch
    {
        "wp_fifo_manager_v1" => new FifoModule(),
        "wp_commit_timing_manager_v1" => new CommitTimingModule(),
        "xdg_activation_v1" => new ActivationModule(),
        "xdg_system_bell_v1" => new SystemBellModule(),
        _ => throw new ArgumentOutOfRangeException(nameof(wireInterface), wireInterface, "unknown module"),
    };

    private static T Bind<T>(CompositorTestHost host, string wireInterface, uint version)
        where T : WlProxy, IWaylandObject<T>
    {
        T? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}

public sealed class SeatDrivenDefaultTests
{
    [Fact]
    public void A_shortcuts_inhibitor_goes_active_with_the_keyboard_focus_and_inactive_without_it()
    {
        using var host = new CompositorTestHost();
        using var manager = new KeyboardShortcutsInhibitManager(host.Display, host.Compositor, host.Seat);

        var window = MappedToplevel.Map(host, host.Client);
        var proxy = Bind<Basin.Desktop.Protocol.ZwpKeyboardShortcutsInhibitManagerV1>(
            host, "zwp_keyboard_shortcuts_inhibit_manager_v1", 1);
        var inhibitor = proxy.InhibitShortcuts(window.Surface, host.Client.Seat!);
        var active = 0;
        var inactive = 0;
        inhibitor.Active += (_, _) => active++;
        inhibitor.Inactive += (_, _) => inactive++;
        host.PumpUntil(() => manager.IsInhibited(window.ServerSurface));
        Assert.Equal(0, active);

        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpUntil(() => active == 1);
        Assert.True(manager.IsActive(window.ServerSurface));

        host.Seat.Keyboard.NotifyClearFocus();
        host.PumpUntil(() => inactive == 1);
        Assert.False(manager.IsActive(window.ServerSurface));
    }

    [Fact]
    public void A_pointer_lock_activates_when_its_surface_takes_the_pointer_focus()
    {
        using var host = new CompositorTestHost();
        using var manager = new PointerConstraintsManager(host.Display, host.Compositor, host.Seat);

        var window = MappedToplevel.Map(host, host.Client);
        var proxy = Bind<Basin.Desktop.Protocol.ZwpPointerConstraintsV1>(host, "zwp_pointer_constraints_v1", 1);
        var pointer = host.Client.Seat!.GetPointer();
        var locked = proxy.LockPointer(
            window.Surface, pointer, null, Basin.Desktop.Protocol.ZwpPointerConstraintsV1.Lifetime.Persistent);
        var lockedCount = 0;
        var unlockedCount = 0;
        locked.Locked += (_, _) => lockedCount++;
        locked.Unlocked += (_, _) => unlockedCount++;
        host.PumpUntil(() => manager.ConstraintFor(window.ServerSurface) is not null);
        Assert.False(manager.ConstraintFor(window.ServerSurface)!.IsActive);

        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 5, 5);
        host.PumpUntil(() => lockedCount == 1);
        Assert.True(manager.ConstraintFor(window.ServerSurface)!.IsActive);

        host.Seat.Pointer.NotifyClearFocus();
        host.PumpUntil(() => unlockedCount == 1);
    }

    private static T Bind<T>(CompositorTestHost host, string wireInterface, uint version)
        where T : WlProxy, IWaylandObject<T>
    {
        T? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}

public sealed class ScaleDefaultTests
{
    [Fact]
    public void A_fractional_scale_object_reports_the_scale_of_the_output_the_surface_is_on()
    {
        using var host = new CompositorTestHost();
        using var scales = new FractionalScaleManager(host.Display, host.Compositor, host.Layout);

        using (var state = new OutputState())
        {
            Assert.True(host.Output.Commit(state.SetScale(1.5)));
        }

        var window = MappedToplevel.Map(host, host.Client);
        var proxy = Bind<Basin.Desktop.Protocol.WpFractionalScaleManagerV1>(
            host, "wp_fractional_scale_manager_v1", 1);
        var fractional = proxy.GetFractionalScale(window.Surface);
        var announced = 0u;
        fractional.PreferredScale += (_, e) => announced = e.Scale;

        window.ServerSurface.SetOutputPresence(host.OutputGlobal, inside: true);
        host.PumpUntil(() => announced == 180);

        Assert.Equal(180u, announced);
    }

    [Fact]
    public void A_consumer_that_announces_a_scale_keeps_the_outputs_from_overruling_it()
    {
        using var host = new CompositorTestHost();
        using var scales = new FractionalScaleManager(host.Display, host.Compositor, host.Layout);

        using (var state = new OutputState())
        {
            Assert.True(host.Output.Commit(state.SetScale(1.5)));
        }

        var window = MappedToplevel.Map(host, host.Client);
        var proxy = Bind<Basin.Desktop.Protocol.WpFractionalScaleManagerV1>(
            host, "wp_fractional_scale_manager_v1", 1);
        var fractional = proxy.GetFractionalScale(window.Surface);
        var announced = 0u;
        fractional.PreferredScale += (_, e) => announced = e.Scale;

        scales.AnnounceScale(window.ServerSurface, 2.0);
        host.PumpUntil(() => announced == 240);

        window.ServerSurface.SetOutputPresence(host.OutputGlobal, inside: true);
        host.PumpToClient();

        Assert.Equal(240u, announced);
    }

    private static T Bind<T>(CompositorTestHost host, string wireInterface, uint version)
        where T : WlProxy, IWaylandObject<T>
    {
        T? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}

public sealed class RelativePointerDefaultTests
{
    [Fact]
    public void The_seat_alone_delivers_relative_motion()
    {
        using var host = new CompositorTestHost();
        using var manager = new RelativePointerManager(host.Display, host.Seat);

        var window = MappedToplevel.Map(host, host.Client);
        var pointer = host.Client.Seat!.GetPointer();
        var proxy = Bind<Basin.Desktop.Protocol.ZwpRelativePointerManagerV1>(
            host, "zwp_relative_pointer_manager_v1", 1);
        var relative = proxy.GetRelativePointer(pointer);
        double dx = 0;
        double dy = 0;
        relative.RelativeMotion += (_, e) =>
        {
            dx += e.Dx.ToDouble();
            dy += e.Dy.ToDouble();
        };
        host.PumpToServer();

        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10, 10);
        host.Seat.Pointer.NotifyMotion(1, 14, 17);
        host.PumpUntil(() => dx != 0);

        Assert.Equal(4, dx);
        Assert.Equal(7, dy);
    }

    [Fact]
    public void A_consumer_that_reports_motion_keeps_the_seat_from_reporting_it_twice()
    {
        using var host = new CompositorTestHost();
        using var manager = new RelativePointerManager(host.Display, host.Seat);

        var window = MappedToplevel.Map(host, host.Client);
        var pointer = host.Client.Seat!.GetPointer();
        var proxy = Bind<Basin.Desktop.Protocol.ZwpRelativePointerManagerV1>(
            host, "zwp_relative_pointer_manager_v1", 1);
        var relative = proxy.GetRelativePointer(pointer);
        var events = 0;
        relative.RelativeMotion += (_, _) => events++;
        host.PumpToServer();

        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10, 10);
        manager.NotifyMotion(1000, 4, 7, 3, 6);
        host.PumpUntil(() => events == 1);

        host.Seat.Pointer.NotifyMotion(1, 14, 17);
        host.PumpToClient();

        Assert.Equal(1, events);
    }

    private static T Bind<T>(CompositorTestHost host, string wireInterface, uint version)
        where T : WlProxy, IWaylandObject<T>
    {
        T? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}

public sealed class PresentationClockTests
{
    [Fact]
    public void EndFrame_reports_presentation_for_a_consumer_that_hooks_nothing_else()
    {
        using var host = new CompositorTestHost();
        var frames = new FrameClock();
        using var pump = new PresentationFeedbackPump(host.Presentation, host.Layout);
        frames.Add(pump);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(4, 4, Fill.Solid(4, 4, 0xFF123456));
        var feedback = client.Presentation!.Feedback(surface);
        ulong presentedTime = 0;
        var presented = false;
        feedback.Presented += (_, e) =>
        {
            presentedTime = (((ulong)e.TvSecHi << 32) | e.TvSecLo) * 1_000_000_000 + e.TvNsec;
            presented = true;
        };

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToClient();

        host.Presentation.SampleAll();
        frames.EndFrame(host.Output, 7_000_000_321);
        host.PumpUntil(() => presented);

        Assert.True(presented);
        Assert.Equal(7_000_000_321ul, presentedTime);
    }
}
