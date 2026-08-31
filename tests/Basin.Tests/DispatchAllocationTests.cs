using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Protocol;
using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class DispatchAllocationTests
{
    private const int Rounds = 100;

    [Fact]
    public void A_commit_carrying_a_frame_callback_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));

        for (var i = 0; i < 3; i++)
        {
            Commits(host, surface, buffer, Rounds);
            host.Loop.Dispatch(0);
            host.Output.StepFrame();
            host.RenderFrame();
        }

        Commits(host, surface, buffer, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", "commit-with-frame-callback", allocated);
    }

    [Fact]
    public void Firing_a_frame_callback_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));

        for (var i = 0; i < 10; i++)
        {
            Commits(host, surface, buffer, 1);
            host.Loop.Dispatch(0);
            host.Output.StepFrame();
            host.RenderFrame();
        }

        Commits(host, surface, buffer, Rounds);
        host.Loop.Dispatch(0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Output.StepFrame();
        host.RenderFrame();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", "frame-callback-fire", allocated);
    }

    [Fact]
    public void An_attach_and_damage_without_a_frame_callback_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));

        for (var i = 0; i < 3; i++)
        {
            Attaches(host, surface, buffer, Rounds);
            host.Loop.Dispatch(0);
            host.Output.StepFrame();
            host.RenderFrame();
        }

        Attaches(host, surface, buffer, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", "attach-and-damage", allocated);
    }

    [Fact]
    public void A_commit_swapping_between_two_buffers_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var front = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        var back = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));

        var allocated = 0L;
        for (var round = 0; round < 2 * Rounds; round++)
        {
            using (var callback = surface.Frame())
            {
                surface.Attach((round % 2 == 0 ? front : back).Proxy, 0, 0);
                surface.Damage(0, 0, 64, 48);
                surface.Commit();
                host.Client.Display.Flush();
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            host.Loop.Dispatch(0);
            var measured = GC.GetAllocatedBytesForCurrentThread() - before;
            host.Output.StepFrame();
            host.RenderFrame();
            if (round >= Rounds)
            {
                allocated += measured;
            }
        }

        Budgets.Check("server", "commit-swapping-buffers", allocated);
    }

    [Fact]
    public void A_popup_map_and_unmap_round_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var client = host.Client;
        var parent = MappedToplevel.Map(host, client);
        var placer = new PopupPlacer(host.Layout);
        var parentTree = new SceneTree(host.Scene.Root);
        host.Shell.NewPopup += popup => placer.Attach(popup, parentTree);
        var buffer = client.CreateBuffer(30, 30, Fill.Solid(30, 30, 0xFF884422));

        for (var i = 0; i < 20; i++)
        {
            PopupRound(host, client, parent, buffer, out _);
        }

        var allocated = 0L;
        for (var round = 0; round < Rounds; round++)
        {
            PopupRound(host, client, parent, buffer, out var measured);
            allocated += measured;
        }

        Budgets.Check("server", "popup-cycle", allocated);
    }

    [Fact]
    public void Setting_a_colour_representation_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var manager = new ColorRepresentationManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);
        var proxy = Bind<Basin.Desktop.Protocol.WpColorRepresentationManagerV1>(
            host, "wp_color_representation_manager_v1", ColorRepresentationManager.Version);
        var representation = proxy.GetSurface(window.Surface);
        host.PumpToClient();

        RepresentationRounds(host, representation, Rounds);
        host.Loop.Dispatch(0);

        RepresentationRounds(host, representation, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", "color-representation-set", allocated);
    }

    [Fact]
    public void A_frame_with_a_client_committing_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(
            new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var window = MappedToplevel.Map(host, host.Client);
        var front = host.Client.CreateBuffer(60, 50, Fill.Gradient(60, 50));
        var back = host.Client.CreateBuffer(60, 50, Fill.Gradient(60, 50));

        for (var round = 0; round < Rounds; round++)
        {
            ClientFrame(host, window, round % 2 == 0 ? front : back, round);
            ServerFrame(host, sceneOutput, swapchain, state, options, round);
        }

        var allocated = 0L;
        for (var round = 0; round < Rounds; round++)
        {
            ClientFrame(host, window, round % 2 == 0 ? front : back, round);
            var before = GC.GetAllocatedBytesForCurrentThread();
            ServerFrame(host, sceneOutput, swapchain, state, options, round);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Budgets.Check("server", "frame-with-a-client", allocated);
    }

    [Fact]
    public void A_layer_surface_repainting_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var layerShell = new Basin.Shell.Xdg.LayerShell(host.Display, host.Compositor);
        var layers = new Basin.Scene.SceneLayers(host.Scene.Root);
        var driver = new LayerShellSceneDriver(layerShell, host.Layout, layers);
        var client = host.Client;

        var shellProxy = Bind<Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1>(host, "zwlr_layer_shell_v1", 4);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(
            surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetSize(200, 30);
        var acked = 0u;
        layerProxy.Configure += (_, e) =>
        {
            acked = e.Serial;
            layerProxy.AckConfigure(e.Serial);
        };
        surface.Commit();
        host.PumpUntil(() => acked != 0);

        SceneSurface? panel = null;
        driver.SceneCreated += (_, scene) => panel = scene;
        var buffer = client.CreateBuffer(200, 30, Fill.Solid(200, 30, 0xFF285577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 200, 30);
        surface.Commit();
        host.PumpUntil(() => panel is not null);

        LayerRepaints(host, surface, buffer, Rounds);
        host.Loop.Dispatch(0);

        LayerRepaints(host, surface, buffer, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", "layer-surface-repaint", allocated);
    }

    [Fact]
    public void Pointer_axis_delivery_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10, 10);
        host.PumpToClient();

        for (var i = 0; i < Rounds; i++)
        {
            AxisRound(host, i);
        }

        host.PumpToClient();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
        {
            AxisRound(host, i);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Budgets.Check("server", "pointer-axis", allocated);
    }

    [Fact]
    public void The_desktop_pack_fan_out_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var foreignToplevels = new ForeignToplevelManager(host.Display, model);
        using var toplevelList = new ForeignToplevelListManager(host.Display, model);
        using var captureSources = new ImageCaptureSourceManager(host.Display);

        var wlr = Bind<Basin.Desktop.Protocol.ZwlrForeignToplevelManagerV1>(
            host, "zwlr_foreign_toplevel_manager_v1", ForeignToplevelManager.Version);
        var ext = Bind<Basin.Desktop.Protocol.ExtForeignToplevelListV1>(
            host, "ext_foreign_toplevel_list_v1", ForeignToplevelListManager.Version);
        Assert.NotNull(wlr);
        Assert.NotNull(ext);

        var id = model.Add("a title", "an.app.id");
        host.PumpToClient();

        for (var i = 0; i < 20; i++)
        {
            model.Reposition(id, new Box(i, i, 100, 100));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
        {
            model.Reposition(id, new Box(i, i, 100, 100));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Budgets.Check("server", "desktop-pack-fan-out", allocated);
    }

    [Fact]
    public void Pointer_delivery_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10, 10);
        host.PumpToClient();

        for (var i = 0; i < 20; i++)
        {
            PointerRound(host, i);
        }

        host.PumpToClient();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
        {
            PointerRound(host, i);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Budgets.Check("server", "pointer-delivery", allocated);
    }

    [Fact]
    public void Key_delivery_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.SetKeymap();
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();

        for (var i = 0; i < 20; i++)
        {
            KeyRound(host, (uint)i);
        }

        host.PumpToClient();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
        {
            KeyRound(host, (uint)i);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Budgets.Check("server", "key-delivery", allocated);
    }

    [Fact]
    public void A_river_repaint_round_stays_within_budget()
    {
        Budgets.Require();

        using var fixture = new RiverFixture();
        var window = fixture.MapToplevel();
        var buffer = fixture.Host.Client.CreateBuffer(40, 40, Fill.Solid(40, 40, 0xff336699));

        for (var i = 0; i < 10; i++)
        {
            RepaintRound(fixture, window, buffer);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
        {
            RepaintRound(fixture, window, buffer);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Budgets.Check("server", "river-repaint-round", allocated);
    }

    [Fact]
    public void Presentation_feedback_on_every_frame_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var pump = new PresentationFeedbackPump(host.Presentation, host.Layout);
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(
            new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var window = MappedToplevel.Map(host, host.Client);
        var front = host.Client.CreateBuffer(60, 50, Fill.Gradient(60, 50));
        var back = host.Client.CreateBuffer(60, 50, Fill.Gradient(60, 50));

        long FeedbackRounds(int rounds)
        {
            var allocated = 0L;
            for (var round = 0; round < rounds; round++)
            {
                var feedback = host.Client.Presentation!.Feedback(window.Surface);
                ClientFrame(host, window, round % 2 == 0 ? front : back, round);

                var before = GC.GetAllocatedBytesForCurrentThread();
                host.Loop.Dispatch(0);
                _ = sceneOutput.Commit(host.Renderer, swapchain, state, options);
                host.Output.StepFrame();
                pump.EndFrame(host.Output, (round + 1) * 16_000_000L);
                host.Scene.SendFrameDone((uint)(round * 16));
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;

                host.PumpToClient();
                feedback.Dispose();
            }

            return allocated;
        }

        _ = FeedbackRounds(Rounds);
        Budgets.Check("server", "presentation-feedback", FeedbackRounds(Rounds));
    }

    [Fact]
    public void A_fifo_and_commit_timing_round_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);
        using var timing = new CommitTimingManager(host.Display, host.Compositor, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(40, 40, Fill.Solid(40, 40, 0xFF336699));
        var fifoProxy = Bind<Basin.Desktop.Protocol.WpFifoManagerV1>(host, "wp_fifo_manager_v1", 1);
        var timingProxy = Bind<Basin.Desktop.Protocol.WpCommitTimingManagerV1>(host, "wp_commit_timing_manager_v1", 1);
        var fifo = fifoProxy.GetFifo(surface);
        var timer = timingProxy.GetTimer(surface);

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        long TimedRounds(int rounds)
        {
            var allocated = 0L;
            for (var round = 0; round < rounds; round++)
            {
                var target = MonotonicClock.Nanos - 1_000_000;
                var seconds = (ulong)(target / 1_000_000_000);
                fifo.WaitBarrier();
                fifo.SetBarrier();
                timer.SetTimestamp((uint)(seconds >> 32), (uint)seconds, (uint)(target % 1_000_000_000));
                surface.Attach(buffer.Proxy, 0, 0);
                surface.Damage(0, 0, 40, 40);
                surface.Commit();
                client.Display.Flush();

                var before = GC.GetAllocatedBytesForCurrentThread();
                host.Loop.Dispatch(0);
                manager.Latch(MonotonicClock.Nanos);
                host.Output.StepFrame();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return allocated;
        }

        _ = TimedRounds(Rounds);
        Budgets.Check("server", "fifo-and-commit-timing", TimedRounds(Rounds));
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int OpenFd(string path, int flags);

    [Fact]
    public void An_explicit_sync_commit_stays_within_budget()
    {
        Budgets.Require();
        Assert.SkipUnless(OperatingSystem.IsLinux(), "explicit sync is DRM syncobjs, which are a Linux object");
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        var drmFd = OpenFd(CompositorTestHost.RenderNodePath, 2);
        Assert.SkipWhen(drmFd < 0, "no render node available");

        try
        {
            var probe = DrmSyncobjTimeline.TryCreate(drmFd);
            Assert.SkipWhen(probe is null, $"{CompositorTestHost.RenderNodePath} has no syncobj support");
            probe!.Release();

            using var host = new CompositorTestHost();
            using var manager = new Basin.Desktop.LinuxDrmSyncobjManager(
                host.Display, host.Compositor, new Basin.Backend.Drm.DrmSyncDevice(drmFd));
            var window = MappedToplevel.Map(host, host.Client);
            var proxy = Bind<Basin.Desktop.Protocol.WpLinuxDrmSyncobjManagerV1>(
                host, "wp_linux_drm_syncobj_manager_v1", 1);

            var acquire = DrmSyncobjTimeline.Create(drmFd);
            var release = DrmSyncobjTimeline.Create(drmFd);
            var acquireFd = acquire.ExportFd();
            var releaseFd = release.ExportFd();
            var acquireProxy = proxy.ImportTimeline(acquireFd);
            var releaseProxy = proxy.ImportTimeline(releaseFd);
            CloseFd(acquireFd);
            CloseFd(releaseFd);
            var syncSurface = proxy.GetSurface(window.Surface);
            host.PumpToServer();

            var front = host.Client.CreateBuffer(60, 50, Fill.Gradient(60, 50));
            var back = host.Client.CreateBuffer(60, 50, Fill.Gradient(60, 50));

            long SyncRounds(int rounds, ulong pointBase)
            {
                var allocated = 0L;
                for (var round = 0; round < rounds; round++)
                {
                    var point = pointBase + (ulong)round + 1;
                    acquire.Signal(point);
                    syncSurface.SetAcquirePoint(acquireProxy, (uint)(point >> 32), (uint)point);
                    syncSurface.SetReleasePoint(releaseProxy, (uint)(point >> 32), (uint)point);
                    window.Surface.Attach((round % 2 == 0 ? front : back).Proxy, 0, 0);
                    window.Surface.Damage(0, 0, 60, 50);
                    window.Surface.Commit();
                    host.Client.Display.Flush();

                    var before = GC.GetAllocatedBytesForCurrentThread();
                    host.Loop.Dispatch(0);
                    allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                }

                return allocated;
            }

            _ = SyncRounds(Rounds, 0);
            var measured = SyncRounds(Rounds, (ulong)Rounds);

            acquire.Release();
            release.Release();
            syncSurface.Dispose();
            acquireProxy.Dispose();
            releaseProxy.Dispose();
            host.PumpToServer();

            Budgets.Check("server", "explicit-sync-commit", measured);
        }
        finally
        {
            CloseFd(drmFd);
        }
    }

    [Fact]
    public void A_screencopy_stream_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        using var manager = new ScreencopyManager(host.Display, host.Layout, host.Buffers, capture);
        _ = new Basin.Scene.SceneRect(host.Scene.Root, 40, 30, new RenderColor(1, 0, 0, 1));

        var proxy = Bind<Basin.Desktop.Protocol.ZwlrScreencopyManagerV1>(host, "zwlr_screencopy_manager_v1", 3);
        var target = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0x00000000));

        long StreamRounds(int rounds)
        {
            var allocated = 0L;
            for (var round = 0; round < rounds; round++)
            {
                var frame = proxy.CaptureOutput(0, host.Client.Outputs[0]);
                var announced = false;
                var done = false;
                frame.Buffer += (_, _) => announced = true;
                frame.Ready += (_, _) => done = true;
                frame.Failed += (_, _) => done = true;
                host.Client.Display.Flush();

                var before = GC.GetAllocatedBytesForCurrentThread();
                host.Loop.Dispatch(0);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;

                host.PumpUntil(() => announced);
                frame.Copy(target.Proxy);
                host.Client.Display.Flush();

                before = GC.GetAllocatedBytesForCurrentThread();
                host.Loop.Dispatch(0);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;

                host.PumpUntil(() => done);
                frame.Dispose();
                host.Client.Display.Flush();

                before = GC.GetAllocatedBytesForCurrentThread();
                host.Loop.Dispatch(0);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return allocated;
        }

        _ = StreamRounds(Rounds);
        Budgets.Check("server", "screencopy-stream", StreamRounds(Rounds));
    }

    [Fact]
    public void A_cursor_motion_round_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(
            new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        using var cursor = new CursorController(host.Layout);
        cursor.AddOutput(host.Output, sceneOutput);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");
        cursor.ShowNamed("left_ptr");

        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10, 10);
        host.PumpToClient();

        long MotionRounds(int rounds)
        {
            var allocated = 0L;
            for (var i = 0; i < rounds; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                host.Seat.Pointer.NotifyMotion((uint)i, 10 + (i % 20), 10 + (i % 20));
                host.Seat.Pointer.NotifyFrame();
                cursor.MoveTo(10 + (i % 20), 10 + (i % 20));
                _ = sceneOutput.Commit(host.Renderer, swapchain, state, options);
                host.Output.StepFrame();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                host.PumpToClient();
            }

            return allocated;
        }

        _ = MotionRounds(Rounds);
        Budgets.Check("server", "cursor-motion", MotionRounds(Rounds));
    }

    [Fact]
    public void Relative_pointer_delivery_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var manager = new RelativePointerManager(host.Display, host.Seat);
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10, 10);

        var proxy = Bind<Basin.Desktop.Protocol.ZwpRelativePointerManagerV1>(
            host, "zwp_relative_pointer_manager_v1", 1);
        var relative = proxy.GetRelativePointer(host.Client.Seat!.GetPointer());
        host.PumpToClient();

        long RelativeRounds(int rounds)
        {
            var allocated = 0L;
            for (var i = 0; i < rounds; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                manager.NotifyMotion((ulong)i * 1000, 2.5, -1.5, 3.0, -2.0);
                host.Seat.Pointer.NotifyMotion((uint)i, 10 + (i % 20), 10 + (i % 20));
                host.Seat.Pointer.NotifyFrame();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                host.PumpToClient();
            }

            return allocated;
        }

        _ = RelativeRounds(Rounds);
        var measured = RelativeRounds(Rounds);
        host.PumpToClient();
        relative.Dispose();

        Budgets.Check("server", "relative-pointer", measured);
    }

    [Fact]
    public void Touch_delivery_over_the_wire_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        var touchProxy = host.Client.Seat!.GetTouch();
        host.PumpToClient();

        long TouchRounds(int rounds)
        {
            var allocated = 0L;
            for (var i = 0; i < rounds; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                host.Seat.Touch.NotifyDown(window.ServerSurface, (uint)i, 0, 10, 10);
                host.Seat.Touch.NotifyFrame();
                host.Seat.Touch.NotifyMotion((uint)i + 1, 0, 12, 12);
                host.Seat.Touch.NotifyFrame();
                host.Seat.Touch.NotifyMotion((uint)i + 2, 0, 14, 14);
                host.Seat.Touch.NotifyFrame();
                host.Seat.Touch.NotifyUp((uint)i + 3, 0);
                host.Seat.Touch.NotifyFrame();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                host.PumpToClient();
            }

            return allocated;
        }

        _ = TouchRounds(Rounds);
        var measured = TouchRounds(Rounds);
        host.PumpToClient();
        touchProxy.Dispose();

        Budgets.Check("server", "touch-delivery", measured);
    }

    [Fact]
    public void A_pointer_gesture_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var manager = new PointerGesturesManager(host.Display, host.Seat);
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 5, 5);

        var proxy = Bind<Basin.Desktop.Protocol.ZwpPointerGesturesV1>(host, "zwp_pointer_gestures_v1", 3);
        var swipe = proxy.GetSwipeGesture(host.Client.Seat!.GetPointer());
        host.PumpToServer();

        long GestureRounds(int rounds)
        {
            var allocated = 0L;
            for (var i = 0; i < rounds; i++)
            {
                var time = (uint)(i * 16);
                var before = GC.GetAllocatedBytesForCurrentThread();
                manager.NotifySwipeBegin(time, 3);
                for (var update = 0; update < 10; update++)
                {
                    manager.NotifySwipeUpdate(time + (uint)update, 4.5, -2.25);
                }

                manager.NotifySwipeEnd(time + 11);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                host.PumpToClient();
            }

            return allocated;
        }

        _ = GestureRounds(Rounds);
        var measured = GestureRounds(Rounds);
        host.PumpToClient();
        swipe.Dispose();

        Budgets.Check("server", "pointer-gesture", measured);
    }

    [Fact]
    public void A_nested_frame_stays_within_budget()
    {
        Budgets.Require();
        CompositorTestHost.SkipWithoutWaylandClient();

        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();
        var front = new MemoryBuffer(output.CurrentMode.Width, output.CurrentMode.Height, DrmFormat.Xrgb8888);
        var back = new MemoryBuffer(output.CurrentMode.Width, output.CurrentMode.Height, DrmFormat.Xrgb8888);
        using var state = new OutputState();
        using var damage = new Pixman.PixmanRegion32();

        long NestedRounds(int rounds)
        {
            var allocated = 0L;
            for (var round = 0; round < rounds; round++)
            {
                state.Clear();
                damage.Reset(new Pixman.PixmanBox32(0, round % 4, 100, (round % 4) + 40));
                state.SetBuffer(round % 2 == 0 ? front : back).SetDamage(damage);

                var before = GC.GetAllocatedBytesForCurrentThread();
                _ = output.Commit(state);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;

                host.Pump(1);
            }

            return allocated;
        }

        _ = NestedRounds(Rounds);
        var measured = NestedRounds(Rounds);

        front.Destroy();
        back.Destroy();
        Budgets.Check("server", "nested-frame", measured);
    }

    [Fact]
    public void An_effect_animation_frame_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        window.SetPosition(40, 30);
        _ = new SceneRect(window, 60, 40, new RenderColor(0.8f, 0.4f, 0.2f, 1f));
        var stack = new TransformStack(window);
        var effect = new Basin.Effects.WobblyEffect();
        effect.Attach(stack);

        long EffectRounds(int rounds, int phase)
        {
            var allocated = 0L;
            for (var i = 0; i < rounds; i++)
            {
                if (!effect.IsWobbling)
                {
                    effect.Grab(30, 20);
                    effect.NotifyMoved(20 + (i % 10), 0);
                    effect.Release();
                }

                var tick = new FrameTick((phase + i) * 16_000_000L, 16_666_667);
                var before = GC.GetAllocatedBytesForCurrentThread();
                _ = effect.Step(tick);
                host.CommitFrame();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return allocated;
        }

        _ = EffectRounds(Rounds, 0);
        Budgets.Check("server", "effect-animation-frame", EffectRounds(Rounds, Rounds));
    }

    [Fact]
    public void A_transaction_configure_round_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();

        long TransactionRounds(int rounds)
        {
            var allocated = 0L;
            for (var i = 0; i < rounds; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                using (var transaction = new Transaction(host.Loop))
                {
                    var first = transaction.Join();
                    var second = transaction.Join();
                    transaction.Seal();
                    first.Ready();
                    second.Ready();
                    host.Loop.Dispatch(0);
                }

                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return allocated;
        }

        _ = TransactionRounds(Rounds);
        Budgets.Check("server", "transaction-configure-round", TransactionRounds(Rounds));
    }

    private sealed class TintStage : IPostStage
    {
        public void Render(IRenderPass pass, ITexture frame, in PostContext context)
        {
            pass.AddTexture(frame, new TextureRenderOptions { DstBox = new Box(0, 0, context.Width, context.Height) });
            pass.AddRect(new RenderColor(0.2f, 0f, 0f, 0.2f), new Box(0, 0, 16, 16));
        }
    }

    [Fact]
    public void A_post_stage_frame_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        host.SceneOutput.AddPostStage(new TintStage());
        var node = new SceneRect(host.Scene.Root, 30, 30, new RenderColor(0.2f, 0.5f, 0.8f, 1f));

        long PostRounds(int rounds)
        {
            var allocated = 0L;
            for (var i = 0; i < rounds; i++)
            {
                node.SetPosition(i % 2, 0);
                var before = GC.GetAllocatedBytesForCurrentThread();
                host.CommitFrame();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return allocated;
        }

        _ = PostRounds(Rounds);
        var measured = PostRounds(Rounds);
        node.Destroy();
        Budgets.Check("server", "post-stage-frame", measured);
    }

    [Fact]
    public void An_xwayland_event_round_stays_within_budget()
    {
        Budgets.Require();
        Assert.SkipUnless(File.Exists("/usr/bin/Xwayland"), "no Xwayland binary");
        Assert.SkipUnless(File.Exists("/usr/bin/xdotool"), "no xdotool to drive the X side");

        _ = Xdotool(":none", "version");

        using var host = new CompositorTestHost();
        using var shell = new Basin.XWayland.XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new Basin.XWayland.XWaylandServer(host.Display, host.Loop);
        Basin.XWayland.XWaylandWm? wm = null;
        server.Ready += fd => wm = new Basin.XWayland.XWaylandWm(fd, host.Loop, shell, host.Seat);

        using var eyes = new System.Diagnostics.Process();
        eyes.StartInfo = new System.Diagnostics.ProcessStartInfo("/usr/bin/xeyes")
        {
            Environment = { ["DISPLAY"] = server.DisplayName },
            RedirectStandardError = true,
        };
        eyes.Start();

        var deadline = Environment.TickCount64 + 15_000;
        while (wm is null && Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(50);
        }

        try
        {
            Assert.SkipWhen(wm is null, "Xwayland did not come up");

            string? windowId = null;
            while (windowId is null && Environment.TickCount64 < deadline)
            {
                host.Loop.Dispatch(20);
                windowId = Xdotool(server.DisplayName, "search --name xeyes")?.Trim().Split('\n')[0];
                if (windowId is { Length: 0 })
                {
                    windowId = null;
                }
            }

            Assert.SkipWhen(windowId is null, "the X client never mapped");

            long XRounds(int rounds)
            {
                var allocated = 0L;
                for (var i = 0; i < rounds; i++)
                {
                    _ = Xdotool(server.DisplayName, $"windowmove {windowId} {10 + (i % 40)} {10 + (i % 40)}");
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var k = 0; k < 5; k++)
                    {
                        host.Loop.Dispatch(1);
                    }

                    allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                }

                return allocated;
            }

            _ = XRounds(Rounds);
            Budgets.Check("server", "xwayland-event-round", XRounds(Rounds));
        }
        finally
        {
            try
            {
                eyes.Kill();
                eyes.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
            }

            eyes.StandardError.Dispose();

            var drained = Environment.TickCount64 + 2000;
            while (server.IsRunning && Environment.TickCount64 < drained)
            {
                host.Loop.Dispatch(20);
            }

            wm?.Dispose();
        }
    }

    private static string? Xdotool(string display, string arguments)
    {
        using var tool = new System.Diagnostics.Process();
        tool.StartInfo = new System.Diagnostics.ProcessStartInfo("/usr/bin/xdotool")
        {
            Arguments = arguments,
            Environment = { ["DISPLAY"] = display },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        tool.Start();
        var output = tool.StandardOutput.ReadToEnd();
        _ = tool.StandardError.ReadToEnd();
        tool.WaitForExit(5000);
        var succeeded = tool.ExitCode == 0;
        tool.StandardOutput.Dispose();
        tool.StandardError.Dispose();
        return succeeded ? output : null;
    }

    [Fact]
    public void A_window_manager_input_round_stays_within_budget()
    {
        Budgets.Require();

        using var fixture = new RiverFixture();
        _ = fixture.MapToplevel();
        var seat = Assert.Single(fixture.Seats);

        Basin.WindowManager.KeyBinding? binding = null;
        var presses = 0;
        fixture.OnManage = _ =>
        {
            if (binding is null)
            {
                binding = fixture.Client.Bindings.Bind(seat, "q", Basin.Config.Modifiers.Super);
                binding.Pressed += () => presses++;
                binding.Enable();
            }
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.NotNull(binding);
        _ = fixture.PressKey(125);

        long InputRounds(int rounds)
        {
            var allocated = 0L;
            for (var i = 0; i < rounds; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                fixture.Host.Seat.Pointer.NotifyMotion((uint)i, 10 + (i % 20), 10 + (i % 20));
                fixture.PressKey(16);
                fixture.ReleaseKey(16);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return allocated;
        }

        _ = InputRounds(Rounds);
        var measured = InputRounds(Rounds);
        Assert.True(presses >= 2 * Rounds);
        fixture.ReleaseKey(125);

        Budgets.Check("client", "wm-input-round", measured);
    }

    [Fact]
    public void A_window_manager_chrome_repaint_stays_within_budget()
    {
        Budgets.Require();

        using var fixture = new RiverFixture();
        _ = fixture.MapToplevel();
        var window = Assert.Single(fixture.Windows);

        var surface = fixture.Client.Compositor!.CreateSurface();
        Basin.WindowManager.WmDecoration? decoration = null;
        fixture.OnManage = _ => decoration ??= window.CreateDecorationAbove(surface);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.NotNull(decoration);

        var front = fixture.CreateManagerBuffer(120, 24, 0xFF303030);
        var back = fixture.CreateManagerBuffer(120, 24, 0xFF303030);

        long ChromeRounds(int rounds)
        {
            var allocated = 0L;
            for (var i = 0; i < rounds; i++)
            {
                var buffer = i % 2 == 0 ? front : back;
                var before = GC.GetAllocatedBytesForCurrentThread();
                unsafe
                {
                    var pixels = (uint*)buffer.Data;
                    for (var x = 0; x < 120; x++)
                    {
                        pixels[x] = 0xFF000000u | (uint)(i * 8);
                    }
                }

                surface.Attach(buffer.Proxy, 0, 0);
                surface.Damage(0, 0, 120, 24);
                surface.Commit();
                fixture.Settle(2);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return allocated;
        }

        _ = ChromeRounds(Rounds);
        var measured = ChromeRounds(Rounds);

        decoration!.Dispose();
        surface.Destroy();
        fixture.Settle();

        Budgets.Check("client", "wm-chrome-repaint", measured);
    }

    private static void Commits(CompositorTestHost host, WlSurface surface, ClientShmBuffer buffer, int count)
    {
        for (var i = 0; i < count; i++)
        {
            using var callback = surface.Frame();
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 64, 48);
            surface.Commit();
        }

        host.Client.Display.Flush();
    }

    [Fact]
    public void A_dmabuf_import_stays_within_budget()
    {
        Budgets.Require();
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");

        using var host = new CompositorTestHost();

        var warm = Imports(host, Rounds);
        host.Loop.Dispatch(0);
        Release(host, warm);

        var measured = Imports(host, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Release(host, measured);
        Budgets.Check("server", "dmabuf-import", allocated);
    }

    [Fact]
    public void A_window_manager_layout_round_stays_within_budget()
    {
        Budgets.Require();

        using var fixture = new RiverFixture();
        _ = fixture.MapToplevel();

        var round = 0;
        var measuring = false;
        var allocated = 0L;

        fixture.OnManage = context =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var window in context.Windows)
            {
                window.ProposeDimensions(80 + (round % 20), 60 + (round % 10));
            }

            if (measuring)
            {
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            round++;
        };

        for (var i = 0; i < Rounds; i++)
        {
            fixture.RequestManageAndSettle();
        }

        measuring = true;
        for (var i = 0; i < Rounds; i++)
        {
            fixture.RequestManageAndSettle();
        }

        Budgets.Check("client", "wm-layout-round", allocated);
    }

    [Fact]
    public void A_remote_frame_arriving_over_a_channel_stays_within_budget()
    {
        Budgets.Require();

        const int width = 64;
        const int height = 48;

        using var transport = new Basin.Transport.Waypipe.WaypipeClientTransport();
        using var engine = new Basin.Transport.Waypipe.WaypipeEngine(
            transport, Basin.Transport.Waypipe.WaypipeCompression.None);

        var open = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(4), width * height * 4);
        engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.OpenFile, open);

        var diff = new byte[12 + 8 + 4096];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(4), 8 + 4096);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(8), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(12), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(16), 4096 / 4);

        var protocol = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(protocol, 4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(protocol.AsSpan(4), (8u << 16) | 6u);

        var drain = new byte[4096];
        var slots = new int[8];

        RemoteFrames(engine, transport, diff, protocol, drain, slots, Rounds);

        var before = GC.GetAllocatedBytesForCurrentThread();
        RemoteFrames(engine, transport, diff, protocol, drain, slots, Rounds);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("channel", "remote-frame", allocated);
    }

    [Fact]
    public void A_remote_dmabuf_frame_arriving_over_a_channel_stays_within_budget()
    {
        Budgets.Require();

        const int width = 64;
        const int height = 48;

        using var transport = new Basin.Transport.Waypipe.WaypipeClientTransport();
        using var engine = new Basin.Transport.Waypipe.WaypipeEngine(
            transport,
            Basin.Transport.Waypipe.WaypipeCompression.None,
            options: new Basin.Transport.Waypipe.WaypipeChannelOptions { CarriesDmabuf = true });

        var open = new byte[8 + 64];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(4), width * height * 4);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(8), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(12), height);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(16), (uint)DrmFormat.Xrgb8888);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(20), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(40), width * 4);
        open[64] = 1;
        engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.OpenDmabuf, open);

        var diff = new byte[12 + 8 + 4096];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(4), 8 + 4096);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(8), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(12), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(16), 4096 / 4);

        var protocol = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(protocol, 4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(protocol.AsSpan(4), (8u << 16) | 6u);

        var drain = new byte[4096];
        var slots = new int[8];

        RemoteFrames(engine, transport, diff, protocol, drain, slots, Rounds);

        var before = GC.GetAllocatedBytesForCurrentThread();
        RemoteFrames(engine, transport, diff, protocol, drain, slots, Rounds);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("channel", "remote-dmabuf-frame", allocated);
    }

    private static void RemoteFrames(
        Basin.Transport.Waypipe.WaypipeEngine engine,
        Basin.Transport.Waypipe.WaypipeClientTransport transport,
        byte[] diff,
        byte[] protocol,
        byte[] drain,
        int[] slots,
        int count)
    {
        for (var i = 0; i < count; i++)
        {
            engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.BufferDiff, diff);
            engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.Protocol, protocol);
            transport.TryReadNonBlocking(drain, Memory<byte>.Empty, slots, Memory<int>.Empty);
        }
    }

    private static void Attaches(CompositorTestHost host, WlSurface surface, ClientShmBuffer buffer, int count)
    {
        for (var i = 0; i < count; i++)
        {
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 64, 48);
            surface.Commit();
        }

        host.Client.Display.Flush();
    }

    private sealed class Imported
    {
        public readonly List<WlBuffer> Buffers = [];

        public readonly List<ZwpLinuxBufferParamsV1> Params = [];

        public readonly List<int> Fds = [];
    }

    private static Imported Imports(CompositorTestHost host, int count)
    {
        const int Width = 64;
        const int Height = 48;
        const int Stride = Width * 4;

        var made = new Imported();
        for (var i = 0; i < count; i++)
        {
            var fd = PlaneFd(Stride * Height);
            var parameters = host.Client.Dmabuf!.CreateParams();
            parameters.Add(fd, 0, 0, Stride, 0, 0);
            parameters.Created += (_, e) => made.Buffers.Add(e.Buffer);
            parameters.Create(Width, Height, (uint)DrmFormat.Argb8888, 0);
            made.Params.Add(parameters);
            made.Fds.Add(fd);
        }

        host.Client.Display.Flush();
        return made;
    }

    private static void Release(CompositorTestHost host, Imported made)
    {
        host.PumpToClient();

        foreach (var buffer in made.Buffers)
        {
            buffer.Dispose();
        }

        foreach (var parameters in made.Params)
        {
            parameters.Dispose();
        }

        foreach (var fd in made.Fds)
        {
            CloseFd(fd);
        }

        host.PumpToClient();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int CloseFd(int fd);

    private static int PlaneFd(int size)
    {
        var fd = memfd_create("basin-budget-plane", 1);
        Assert.True(fd >= 0);
        Assert.Equal(0, ftruncate(fd, size));
        return fd;
    }

    private static void PopupRound(
        CompositorTestHost host,
        ShmTestClient client,
        MappedToplevel parent,
        ClientShmBuffer buffer,
        out long allocated)
    {
        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(30, 30);
        positioner.SetAnchorRect(5, 5, 1, 1);
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase.GetXdgSurface(surface);
        var popup = xdgSurface.GetPopup(parent.XdgSurface, positioner);
        xdgSurface.Configure += (_, e) => xdgSurface.AckConfigure(e.Serial);
        surface.Commit();
        client.Display.Flush();

        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        host.PumpToClient();

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 30, 30);
        surface.Commit();
        popup.Destroy();
        xdgSurface.Destroy();
        surface.Dispose();
        positioner.Destroy();
        client.Display.Flush();

        before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        allocated += GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void RepresentationRounds(
        CompositorTestHost host,
        Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1 representation,
        int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            representation.SetAlphaMode(i % 2 == 0
                ? Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.AlphaMode.Straight
                : Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.AlphaMode.PremultipliedElectrical);
            representation.SetCoefficientsAndRange(
                Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.Coefficients.Bt709,
                i % 2 == 0
                    ? Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.Range.Limited
                    : Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.Range.Full);
            representation.SetChromaLocation(
                Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.ChromaLocation.Type0);
        }

        host.Client.Display.Flush();
    }

    private static void ClientFrame(
        CompositorTestHost host, MappedToplevel window, ClientShmBuffer buffer, int round)
    {
        using var callback = window.Surface.Frame();
        window.Surface.Attach(buffer.Proxy, 0, 0);
        window.Surface.Damage(0, round % 4, 60, 20);
        window.Surface.Commit();
        host.Client.Display.Flush();
    }

    private static void ServerFrame(
        CompositorTestHost host,
        SceneOutput sceneOutput,
        Swapchain swapchain,
        OutputState state,
        in SceneCommitOptions options,
        int round)
    {
        host.Loop.Dispatch(0);
        _ = sceneOutput.Commit(host.Renderer, swapchain, state, options);
        host.Output.StepFrame();
        host.Scene.SendFrameDone((uint)(round * 16));
    }

    private static void LayerRepaints(
        CompositorTestHost host, WlSurface surface, ClientShmBuffer buffer, int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, i % 4, 200, 10);
            surface.Commit();
        }

        host.Client.Display.Flush();
    }

    private static void AxisRound(CompositorTestHost host, int i)
    {
        host.Seat.Pointer.NotifyAxis(
            (uint)i,
            new PointerAxis(WlPointer.Axis.VerticalScroll, 10 + (i % 5), 120));
        host.Seat.Pointer.NotifyFrame();
    }

    private static void PointerRound(CompositorTestHost host, int i)
    {
        host.Seat.Pointer.NotifyMotion((uint)i, 10 + (i % 20), 10 + (i % 20));
        host.Seat.Pointer.NotifyFrame();
    }

    private static void KeyRound(CompositorTestHost host, uint i)
    {
        host.Seat.Keyboard.NotifyKey(i, 30, true);
        host.Seat.Keyboard.NotifyKey(i + 1, 30, false);
    }

    private static void RepaintRound(RiverFixture fixture, MappedToplevel window, ClientShmBuffer buffer)
    {
        window.Surface.Attach(buffer.Proxy, 0, 0);
        window.Surface.Damage(0, 0, 40, 40);
        window.Surface.Commit();
        fixture.Settle(2);
    }

    private static T Bind<T>(CompositorTestHost host, string wireInterface, int version)
        where T : WlProxy, IWaylandObject<T>
    {
        T? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, (uint)version);
            }
        };

        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}
