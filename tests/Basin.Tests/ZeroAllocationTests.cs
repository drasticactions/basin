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

    [Fact]
    public void Shadow_nine_patch_allocates_nothing_over_1000_frames()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ShadowManager(host.Display, host.Compositor);
        Basin.Plasma.Protocol.OrgKdeKwinShadowManager? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_shadow_manager")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinShadowManager>(e.Name, 2);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var surface = host.Client.Compositor.CreateSurface();
        var content = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(content.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        var scene = host.SurfaceScenes[0];
        scene.Tree.SetPosition(40, 30);
        using var effect = new Basin.Plasma.ShadowEffect(scene, manager);

        var shadow = proxy!.Create(surface);
        var corner = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFF404040));
        var edge = host.Client.CreateBuffer(8, 16, Fill.Solid(8, 16, 0xFF303030));
        var side = host.Client.CreateBuffer(16, 8, Fill.Solid(16, 8, 0xFF303030));
        shadow.AttachTopLeft(corner.Proxy);
        shadow.AttachTopRight(corner.Proxy);
        shadow.AttachBottomLeft(corner.Proxy);
        shadow.AttachBottomRight(corner.Proxy);
        shadow.AttachTop(edge.Proxy);
        shadow.AttachBottom(edge.Proxy);
        shadow.AttachLeft(side.Proxy);
        shadow.AttachRight(side.Proxy);
        shadow.SetLeftOffset(Wayland.WlFixed.FromInt(16));
        shadow.SetTopOffset(Wayland.WlFixed.FromInt(16));
        shadow.SetRightOffset(Wayland.WlFixed.FromInt(16));
        shadow.SetBottomOffset(Wayland.WlFixed.FromInt(16));
        shadow.Commit();
        surface.Commit();
        host.PumpToServer();

        for (var i = 0; i < 20; i++)
        {
            Frame(host);
        }

        NothingAllocated(1000, _ => Frame(host));
    }

    [Fact]
    public void Slide_animation_allocates_nothing_over_1000_frames()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.SlideManager(host.Display, host.Compositor);
        Basin.Plasma.Protocol.OrgKdeKwinSlideManager? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_slide_manager")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinSlideManager>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var scene = host.SurfaceScenes[0];
        scene.Tree.SetPosition(40, 30);
        using var effect = new Basin.Plasma.SlideEffect(scene, manager, _ => host.Layout.Bounds);
        effect.DurationNanos = 60_000_000_000;

        var slide = proxy!.Create(surface);
        slide.SetLocation((uint)Basin.Plasma.Protocol.OrgKdeKwinSlide.Location.Bottom);
        slide.Commit();
        var content = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(content.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        Assert.True(effect.IsAnimating);

        var millis = 0L;
        void Round()
        {
            effect.Step(new Scene.FrameTick(millis * 1_000_000, 16_666_667));
            millis += 16;
            Frame(host);
        }

        for (var i = 0; i < 20; i++)
        {
            Round();
        }

        NothingAllocated(1000, _ => Round());
        Assert.True(effect.IsAnimating);
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
        Assert.SkipWhen(
            renderer is "vulkan" or "skia",
            $"the {renderer} shm upload path still allocates once a swap");
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

    private static void Frame(CompositorTestHost host)
    {
        host.Output.StepFrame();
        host.RenderFrame();
        host.Loop.Dispatch(0);
    }
}
