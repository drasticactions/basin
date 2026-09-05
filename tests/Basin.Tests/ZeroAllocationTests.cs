using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class ZeroAllocationTests
{
    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    [InlineData("skia-gl")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    [InlineData("impeller")]
    public void Frame_loop_allocates_nothing_over_1000_frames(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        _ = new Scene.SceneRect(host.Scene.Root, 30, 30, new RenderColor(0.2f, 0.5f, 0.8f, 1f));

        for (var i = 0; i < 20; i++)
        {
            Frame(host);
        }

        NothingAllocated(1000, _ => Frame(host));
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    [InlineData("skia-gl")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    [InlineData("impeller")]
    public void SceneOutput_frame_loop_allocates_nothing_over_1000_frames(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        var node = new Scene.SceneRect(host.Scene.Root, 30, 30, new RenderColor(0.2f, 0.5f, 0.8f, 1f));

        for (var i = 0; i < 20; i++)
        {
            node.SetPosition(i % 2, 0);
            host.CommitFrame();
        }

        NothingAllocated(1000, i =>
        {
            node.SetPosition(i % 2, 0);
            host.CommitFrame();
        });
        node.Destroy();
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    [InlineData("skia-gl")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    [InlineData("impeller")]
    public void SceneOutput_direct_scanout_allocates_nothing_over_1000_frames(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var client = DirectScanoutTests.FakeClientBuffer(160, 120);
        using var clientGuard = new DeferDestroy(client);
        var node = new Scene.SceneBuffer(host.Scene.Root) { IsOpaque = true };
        node.SetBuffer(client);
        var options = new Scene.SceneCommitOptions { AllowDirectScanout = true };

        for (var i = 0; i < 20; i++)
        {
            host.CommitFrame(options);
        }

        Assert.SkipWhen(
            host.SceneOutput.ScanoutCommits == 0,
            $"the {renderer} row never entered direct scanout");

        NothingAllocated(1000, _ => host.CommitFrame(options));
        Assert.True(host.SceneOutput.ScanoutCommits >= 1000);
        node.Destroy();
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    [InlineData("skia-gl")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    [InlineData("impeller")]
    public void SceneOutput_plane_offload_allocates_nothing_over_1000_frames(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var output = new PlaneOutput();
        using (var lit = new OutputState())
        {
            Assert.True(output.Commit(lit.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000))));
        }

        using var sceneOutput = new Scene.SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(
            new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new Scene.SceneCommitOptions { AllowPlaneOffload = true };

        var background = new Scene.SceneRect(host.Scene.Root, 160, 120, new RenderColor(0.1f, 0.2f, 0.3f, 1f));
        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        using var clientGuard = new DeferDestroy(client);
        var node = new Scene.SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);
        output.Accept = (_, _) => true;

        void Frame(int i)
        {
            background.SetPosition(i % 2, 0);
            host.Loop.Dispatch(0);
            _ = sceneOutput.Commit(host.Renderer, swapchain, state, options);
        }

        for (var i = 0; i < 20; i++)
        {
            Frame(i);
        }

        Assert.SkipWhen(
            sceneOutput.OffloadCommits == 0,
            $"the {renderer} row never offloaded a plane");

        var before = sceneOutput.OffloadCommits;
        NothingAllocated(1000, Frame);
        Assert.True(sceneOutput.OffloadCommits - before >= 1000);
        node.Destroy();
        background.Destroy();
        output.Destroy();
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    [InlineData("skia-gl")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    [InlineData("impeller")]
    public void Two_outputs_at_different_descriptions_allocate_nothing_over_1000_frames(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        using var resolver = new SceneColorTableTests.PairResolver(host.Renderer);
        host.Scene.ColorTransforms = resolver;
        host.SceneOutput.ColorDescription = new ImageDescription
        {
            PrimariesNamed = ColorPrimaries.DisplayP3,
            TransferNamed = ColorTransferFunction.Gamma22,
        };
        using var secondScene = new Scene.SceneOutput(host.Scene, second)
        {
            ColorDescription = new ImageDescription
            {
                PrimariesNamed = ColorPrimaries.Bt2020,
                TransferNamed = ColorTransferFunction.St2084Pq,
            },
        };
        using var secondSwapchain = new Swapchain(
            new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var secondState = new OutputState();

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        var node = new Scene.SceneRect(host.Scene.Root, 30, 30, new RenderColor(0.2f, 0.5f, 0.8f, 1f));

        void Frame(int i)
        {
            node.SetPosition(i % 2, 0);
            host.CommitFrame();
            _ = secondScene.Commit(host.Renderer, secondSwapchain, secondState);
            second.StepFrame();
        }

        for (var i = 0; i < 20; i++)
        {
            Frame(i);
        }

        NothingAllocated(1000, Frame);
        node.Destroy();
    }

    private sealed class PlaneOutput() : OutputBase("plane-test")
    {
        public Func<OutputLayer, int, bool>? Accept { get; set; }

        protected override bool SupportsLayers => true;

        protected override bool TestCommitCore(OutputState state) => Judge(state);

        protected override bool CommitCore(OutputState state) => Judge(state);

        private bool Judge(OutputState state)
        {
            if ((state.Fields & OutputStateFields.Layers) == 0 || state.Layers is null)
            {
                return true;
            }

            for (var i = 0; i < state.Layers.Count; i++)
            {
                state.Layers[i].Accepted = Accept?.Invoke(state.Layers[i], i) ?? false;
            }

            return true;
        }
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public void Animated_shader_uniforms_allocate_nothing_over_1000_frames(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(PixelShaderTests.RingSource, PixelShaderTests.RingUniforms);
        Assert.NotNull(shader);

        var node = new Scene.SceneShader(host.Scene.Root) { Shader = shader, Bounds = new Box(4, 4, 100, 80) };
        var values = new PixelShaderUniformValue[] { (50f, 40f), 30f };
        for (var i = 0; i < 20; i++)
        {
            shader!.SetUniforms(values);
            node.NotifyShaderChanged();
            Frame(host);
        }

        NothingAllocated(1000, i =>
        {
            values[1] = 20f + (i % 20);
            shader!.SetUniforms(values);
            node.NotifyShaderChanged();
            Frame(host);
        });
        node.Destroy();
    }

    [Fact]
    public void Frame_path_capabilities_allocate_nothing_over_1000_frames()
    {
        using var host = new CompositorTestHost();
        var capture = new Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        var theme = new Capabilities.Defaults.CursorImageTheme();
        Capabilities.IScreenCapture screenCapture = capture;
        Capabilities.IDmabufCapture dmabufCapture = new Scene.SceneDmabufCapture();
        Capabilities.ICursorTheme cursorTheme = theme;

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var source = Capabilities.CaptureSource.Output(host.Output);
        for (var i = 0; i < 20; i++)
        {
            DriveCapabilities(host, screenCapture, dmabufCapture, cursorTheme, source);
        }

        NothingAllocated(1000, _ => DriveCapabilities(host, screenCapture, dmabufCapture, cursorTheme, source));
    }

    [Fact]
    public void Output_capture_with_an_exclusion_allocates_nothing()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        var index = new Scene.ToplevelSceneIndex();
        var capture = new Scene.SceneScreenCapture(host.Scene, host.Layout)
        {
            Renderer = host.Renderer,
            Toplevels = model,
            Index = index,
        };
        var tree = new Scene.SceneTree(host.Scene.Root);
        _ = new Scene.SceneRect(tree, 32, 32, new RenderColor(1, 0, 0, 1));
        var id = model.Add("secret", "app.secret");
        index.Set(id, new Scene.ToplevelCaptureTrees(tree, null));
        model.SetState(id, Capabilities.ToplevelState.ExcludedFromCapture);

        Capabilities.IScreenCapture screenCapture = capture;
        var source = Capabilities.CaptureSource.Output(host.Output);
        for (var i = 0; i < 20; i++)
        {
            _ = screenCapture.Capture(source, default, host.Target);
        }

        NothingAllocated(1000, round => screenCapture.Capture(source, default, host.Target));
        tree.Destroy();
    }

    private static void DriveCapabilities(
        CompositorTestHost host,
        Capabilities.IScreenCapture capture,
        Capabilities.IDmabufCapture dmabuf,
        Capabilities.ICursorTheme theme,
        in Capabilities.CaptureSource source)
    {
        _ = capture.Supports(source);
        _ = capture.TryDescribe(source, out _);
        _ = capture.Capture(source, default, host.Target);
        _ = capture.TryCursorState(host.Output, out _);
        _ = dmabuf.TryCurrentFrame(host.Output, out _);
        _ = theme.TryResolve("left_ptr", 1, out _);
        _ = theme.TryResolve(Capabilities.CursorShape.Grab, 1, out _);
    }

    [Fact]
    public void Surface_presence_tracking_allocates_nothing_over_1000_walks()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(32, 32, Fill.Solid(32, 32, 0xffff0000));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var announced = 0;
        var tracker = new SurfacePresenceTracker(host.Layout, (_, _) => announced++);
        tracker.AddOutput(host.Output, host.OutputGlobal);
        var presence = new List<SurfaceBox>();

        void Walk(int i)
        {
            host.Scene.CollectSurfaces(presence);
            tracker.Update(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(presence));
        }

        for (var i = 0; i < 20; i++)
        {
            Walk(i);
        }

        NothingAllocated(1000, Walk);
        Assert.True(announced >= 1000);
    }

    [Fact]
    public void Touch_routing_allocates_nothing_over_1000_cycles()
    {
        using var host = new CompositorTestHost();
        var screen = host.Backend.CreateTouchScreen();
        var chrome = new CountingChrome();
        var target = new CountingPointerTarget();
        var idle = new Seat.SeatIdleSource();
        var router = new Seat.TouchRouter(host.Seat.Touch)
        {
            Chrome = chrome,
            Gestures = new Seat.CentroidSwipeGesture { Fingers = 3 },
            Pointer = new Seat.TouchPointerDriver(host.Seat.Touch, target) { ClaimWithoutSurface = true },
            Activity = idle,
        };
        screen.Down += (time, slot, x, y) => router.Down(time, slot, x * 160, y * 120);
        screen.Motion += (time, slot, x, y) => router.Motion(time, slot, x * 160, y * 120);
        screen.Up += router.Up;
        screen.Frame += router.Frame;
        screen.Cancel += router.Cancel;

        void Cycle(int i)
        {
            chrome.Take = true;
            screen.InjectDown(1, 0, 0.1, 0.1);
            screen.InjectMotion(2, 0, 0.2, 0.2);
            screen.InjectFrame();
            screen.InjectUp(3, 0);
            screen.InjectFrame();
            chrome.Take = false;
            screen.InjectDown(4, 1, 0.8, 0.8);
            screen.InjectMotion(5, 1, 0.9, 0.9);
            screen.InjectUp(6, 1);
            screen.InjectFrame();
        }

        for (var i = 0; i < 20; i++)
        {
            Cycle(i);
        }

        NothingAllocated(1000, Cycle);
        Assert.True(chrome.Presses >= 1000);
        Assert.True(target.Buttons >= 2000);
    }

    private sealed class CountingChrome : Seat.ITouchChrome
    {
        public bool Take { get; set; }

        public int Presses { get; private set; }

        public bool TryPress(int id, uint timeMs, double x, double y)
        {
            if (!Take)
            {
                return false;
            }

            Presses++;
            return true;
        }

        public void Motion(int id, uint timeMs, double x, double y)
        {
        }

        public void Release(int id, uint timeMs, double x, double y)
        {
        }

        public void Cancel()
        {
        }
    }

    private sealed class CountingPointerTarget : Seat.ITouchPointerTarget
    {
        public int Buttons { get; private set; }

        public void Warp(uint timeMs, double x, double y)
        {
        }

        public void Button(uint timeMs, uint button, bool pressed) => Buttons++;
    }

    [Fact]
    public void Frame_loop_allocates_nothing_at_fractional_scale()
    {
        using var host = new CompositorTestHost();
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetScale(1.5)));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        _ = new Scene.SceneRect(host.Scene.Root, 30, 30, new RenderColor(0.2f, 0.5f, 0.8f, 1f));

        for (var i = 0; i < 20; i++)
        {
            Frame(host);
        }

        NothingAllocated(1000, _ => Frame(host));
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    [InlineData("skia-gl")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    [InlineData("impeller")]
    public void Decorated_idle_window_allocates_nothing(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        using var theme = new TestFrameTheme();
        using var uiHost = new Basin.UI.Skia.SkiaUIHost();
        var frame = new Basin.Scene.Frame(uiHost, new TestFrameRenderer(theme), host.Scene.Root);
        var geometry = new Box(20, 40, 64, 48);
        var client = new Scene.SceneRect(host.Scene.Root, geometry.Width, geometry.Height, new RenderColor(0.8f, 0.5f, 0.2f, 1f));
        client.SetPosition(geometry.X, geometry.Y);
        frame.Configure(geometry, 1.0, new Basin.Capabilities.FrameState { Title = "基盤 Basin", Active = true });
        frame.Commit();

        for (var i = 0; i < 20; i++)
        {
            Frame(host);
        }

        NothingAllocated(1000, _ => Frame(host));
        frame.Dispose();
    }

    [Fact]
    public void Seat_input_into_a_ui_surface_allocates_nothing()
    {
        using var uiHost = new Basin.UI.Skia.SkiaUIHost();
        var surface = uiHost.CreateSurface(new Basin.Capabilities.UISurfaceOptions
        {
            Target = Basin.Capabilities.UITargetKind.Memory,
            Width = 96,
            Height = 32,
            Scale = 1.0,
        });
        Assert.NotNull(surface);

        var pressed = new uint[] { 30, 31 };
        var text = "漢字".ToCharArray();
        for (var i = 0; i < 20; i++)
        {
            Deliver(surface, pressed, text, i);
        }

        NothingAllocated(1000, i => Deliver(surface, pressed, text, i));
        surface.Dispose();
    }

    private static void Deliver(Basin.Capabilities.IUISurface surface, uint[] pressed, char[] text, int i)
    {
        var time = (uint)i;
        surface.NotifyKeyboardEnter(pressed);
        surface.NotifyModifiers((uint)(i & 7), 0, 0, 0);
        surface.NotifyKey(time, 30, pressed: true);
        surface.NotifyKey(time, 30, pressed: false);
        surface.NotifyKeyboardLeave();
        surface.NotifyTouchDown(time, 0, i % 32, i % 16);
        surface.NotifyTouchMotion(time, 0, (i + 1) % 32, i % 16);
        surface.NotifyTouchUp(time, 0);
        surface.NotifyTouchCancel();
        surface.NotifyPreedit(text, 0, text.Length);
        surface.NotifyTextCommit(text);
        _ = surface.WantsTextInput;
    }

    [Fact]
    public void The_hosted_tick_allocates_nothing_over_1000_frames()
    {
        using var host = new CompositorTestHost();
        using var backend = new Basin.Backend.Hosted.HostedBackend();
        var output = backend.CreateOutput(new OutputMode(160, 120, 60_000));
        using var sceneOutput = new Scene.SceneOutput(host.Scene, output);
        using var frame = new Basin.Backend.Hosted.HostedFrame(host.Display, host.Loop, sceneOutput);
        var target = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        var node = new Scene.SceneRect(host.Scene.Root, 30, 30, new RenderColor(0.2f, 0.5f, 0.8f, 1f));

        for (var i = 0; i < 20; i++)
        {
            node.SetPosition(i % 2, 0);
            frame.Tick(host.Renderer, target, 1);
        }

        NothingAllocated(1000, i =>
        {
            node.SetPosition(i % 2, 0);
            frame.Tick(host.Renderer, target, 1);
        });

        node.Destroy();
        target.Destroy();
    }

    [Fact]
    public void A_pending_release_callback_costs_nothing_per_frame()
    {
        using var host = new CompositorTestHost();

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        var release = surface.GetRelease();
        var released = false;
        release.Done += (_, _) => released = true;
        surface.Commit();
        host.PumpToServer();

        for (var i = 0; i < 20; i++)
        {
            Frame(host);
        }

        NothingAllocated(1000, _ => Frame(host));
        Assert.False(released, "the buffer is still on screen, so its usage has not ended");

        surface.Destroy();
        host.PumpToServer();
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    [InlineData("skia-gl")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    [InlineData("impeller")]
    public void A_client_swapping_buffers_costs_nothing_to_render(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var surface = host.Client.Compositor.CreateSurface();
        var front = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        var back = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));

        var rounds = new long[220];

        long Pass()
        {
            var allocated = 0L;
            for (var i = 0; i < 220; i++)
            {
                using (var callback = surface.Frame())
                {
                    surface.Attach((i % 2 == 0 ? front : back).Proxy, 0, 0);
                    surface.Damage(0, 0, 64, 48);
                    surface.Commit();
                    host.Client.Display.Flush();
                }

                host.Loop.Dispatch(0);

                var before = GC.GetAllocatedBytesForCurrentThread();
                host.Output.StepFrame();
                host.RenderFrame();
                var round = GC.GetAllocatedBytesForCurrentThread() - before;
                rounds[i] = round;
                if (i >= 20)
                {
                    allocated += round;
                }
            }

            return allocated;
        }

        var first = Pass();
        if (first == 0)
        {
            return;
        }

        var firstRounds = Offenders(rounds, 20);
        var second = Pass();
        if (second == 0)
        {
            Report($"{renderer} allocated {first} bytes on a first pass ({firstRounds}), and nothing on a second");
            return;
        }

        Assert.Fail(
            $"{renderer} allocated {second} bytes after warm-up: {Offenders(rounds, 20)}" +
            $" (a first pass allocated {first} bytes: {firstRounds})");
    }

    private static void NothingAllocated(int rounds, Action<int> round)
    {
        var measured = new long[rounds];
        var first = Pass(round, measured);
        if (first == 0)
        {
            return;
        }

        var firstRounds = Offenders(measured, 0);
        var second = Pass(round, measured);
        if (second == 0)
        {
            Report($"a first pass allocated {first} bytes ({firstRounds}), and a second pass nothing");
            return;
        }

        Assert.Fail(
            $"a second pass allocated {second} bytes: {Offenders(measured, 0)}" +
            $" (a first pass allocated {first} bytes: {firstRounds})");
    }

    private static long Pass(Action<int> round, long[] measured)
    {
        var allocated = 0L;
        for (var i = 0; i < measured.Length; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            round(i);
            measured[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            allocated += measured[i];
        }

        return allocated;
    }

    private static string Offenders(long[] measured, int from) =>
        string.Join(
            ", ",
            Enumerable.Range(from, measured.Length - from)
                .Where(i => measured[i] != 0)
                .Select(i => $"round {i}={measured[i]}"));

    private static void Report(string message) =>
        TestContext.Current.TestOutputHelper?.WriteLine(message);

    [Fact]
    public void A_log_line_allocates_nothing_over_1000_lines()
    {
        var previousLevel = Basin.Diagnostics.BasinLog.Level;
        var previousSink = Basin.Diagnostics.BasinLog.Sink;
        var sink = new CountingLogSink();
        Basin.Diagnostics.BasinLog.Level = Basin.Diagnostics.BasinLogLevel.Debug;
        Basin.Diagnostics.BasinLog.Sink = sink;
        try
        {
            var log = Basin.Diagnostics.BasinLog.For("zero");
            var payload = new char[600];
            payload.AsSpan().Fill('x');

            for (var i = 0; i < 20; i++)
            {
                log.Debug($"an enabled line with {i} and {i * 2} and {i * 3} holes");
                log.Trace($"a line below the threshold with {i} and {i * 2} and {i * 3} holes");
                log.Debug($"a line that outgrows the scratch: {payload.AsSpan()} with {i} holes");
            }

            NothingAllocated(1000, i => log.Debug($"an enabled line with {i} and {i * 2} and {i * 3} holes"));
            NothingAllocated(1000, i => log.Trace($"a line below the threshold with {i} and {i * 2} and {i * 3} holes"));
            NothingAllocated(1000, i => log.Debug($"a line that outgrows the scratch: {payload.AsSpan()} with {i} holes"));

            Assert.True(sink.Writes >= 2040);
            Assert.True(sink.LongestLine > 600);
        }
        finally
        {
            Basin.Diagnostics.BasinLog.Level = previousLevel;
            Basin.Diagnostics.BasinLog.Sink = previousSink;
        }
    }

    private sealed class CountingLogSink : Basin.Diagnostics.IBasinLogSink
    {
        public long Writes { get; private set; }

        public int LongestLine { get; private set; }

        public void Write(
            Basin.Diagnostics.BasinLogLevel level, string category, ReadOnlySpan<char> message)
        {
            Writes++;
            LongestLine = Math.Max(LongestLine, message.Length);
        }
    }

    private static void Frame(CompositorTestHost host)
    {
        host.Output.StepFrame();
        host.RenderFrame();
        host.Loop.Dispatch(0);
    }

    private sealed class DeferDestroy(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }
}
