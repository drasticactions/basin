using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Basin.UI.Skia;
using Xunit;

namespace Basin.Tests;

public sealed class RendererLossTests
{
    private const int Width = 64;
    private const int Height = 48;

    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" };

    public static TheoryData<string> GpuRenderers => new() { "gl", "vulkan", "skia-gl", "skia-vulkan", "skia-graphite" };

    [Theory]
    [MemberData(nameof(Renderers))]
    public void An_injected_loss_rebuilds_the_renderer_once_and_draws_the_same_frame(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        SkipWithoutServer();
        var factories = 0;
        using var rig = new LossRig(() => RendererCatalog.Create(row, CompositorTestHost.RenderNodePath), () => factories++);
        var before = rig.Frame();
        var lost = rig.Driver.Renderer;

        if (!LossInjection.TryInject(lost))
        {
            Assert.Equal(RendererLossDetection.Never, lost.LossDetection);
            rig.DamageAll();
            rig.Pump(() => false, 200);
            Assert.Equal(0, rig.Driver.RendererRebuilds);
            Assert.Same(lost, rig.Driver.Renderer);
            return;
        }

        Assert.NotEqual(RendererLossDetection.Never, lost.LossDetection);
        var replaced = 0;
        rig.Host.RendererReplaced += replacement =>
        {
            replaced++;
            Assert.Same(lost, replacement.Old.Renderer);
        };
        rig.DamageAll();
        rig.Pump(() => rig.Driver.RendererRebuilds > 0);
        Assert.Equal(1, rig.Driver.RendererRebuilds);
        Assert.Equal(1, factories);
        Assert.Equal(1, replaced);
        Assert.NotSame(lost, rig.Driver.Renderer);

        var after = rig.Frame();
        AssertSameFrame(before, after);
    }

    [Theory]
    [MemberData(nameof(GpuRenderers))]
    public void A_rebuild_leaves_no_live_objects_and_no_fds_behind(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        SkipWithoutServer();
        LeakTracking.Require();
        var allowance = DriverFdResidue.For(row);
        TestLogging.WarmStreams();
        BasinCounters.Reset();
        var baseline = FdSnapshot.Take();
        using (var rig = new LossRig(() => RendererCatalog.Create(row, CompositorTestHost.RenderNodePath)))
        {
            rig.Frame();
            Assert.True(LossInjection.TryInject(rig.Driver.Renderer));
            rig.DamageAll();
            rig.Pump(() => rig.Driver.RendererRebuilds > 0);
            Assert.Equal(1, rig.Driver.RendererRebuilds);
            rig.Frame();
        }

        Assert.Equal(0, BasinCounters.LiveObjects);
        Assert.Equal(0, BasinCounters.PendingFrees);
        var leaked = FdSnapshot.Diff(baseline, FdSnapshot.Take(), Doubled(allowance));
        Assert.True(leaked.Count == 0, "fds leaked: " + string.Join(", ", leaked.Select(l => $"{l.Count} x {l.Target}")));
    }

    [Fact]
    public void Two_losses_before_the_rebuild_make_one_rebuild()
    {
        SkipWithoutServer();
        var factories = 0;
        using var rig = new LossRig(() => new RenderStack(new LosingRenderer(), null), () => factories++, outputs: 2);
        rig.Frame();
        var lost = (LosingRenderer)rig.Driver.Renderer;
        lost.RaiseLost();
        lost.RaiseLost();
        rig.DamageAll();
        rig.Pump(() => rig.Driver.RendererRebuilds > 0);
        rig.Pump(() => false, 100);
        Assert.Equal(1, rig.Driver.RendererRebuilds);
        Assert.Equal(1, factories);
        Assert.True(lost.Disposed);
        rig.Frame();
    }

    [Fact]
    public void A_service_that_holds_the_renderer_is_told_before_the_old_one_is_disposed()
    {
        SkipWithoutServer();
        var bound = new BoundProbe();
        using var rig = new LossRig(() => new RenderStack(new LosingRenderer(), null), services: services => services.Use(bound));
        var rebuilt = 0;
        rig.Host.RendererRebuilt += replacement =>
        {
            rebuilt++;
            Assert.True(((LosingRenderer)replacement.Old.Renderer).Disposed);
            Assert.Same(rig.Driver.Renderer, replacement.New.Renderer);
        };
        rig.Frame();
        var lost = (LosingRenderer)rig.Driver.Renderer;
        lost.Lose();
        rig.DamageAll();
        rig.Pump(() => rig.Driver.RendererRebuilds > 0);
        Assert.Equal(1, bound.Calls);
        Assert.False(bound.OldWasDisposed);
        Assert.Equal(1, rebuilt);
    }

    [Fact]
    public void The_scene_capture_follows_the_new_renderer()
    {
        SkipWithoutServer();
        using var rig = new LossRig(() => new RenderStack(new LosingRenderer(), null));
        var pack = new SceneCapturePack(rig.Scene, rig.Layout);
        pack.Capture.Renderer = rig.Driver.Renderer;
        rig.Driver.Capture = pack;
        rig.Frame();
        var lost = (LosingRenderer)rig.Driver.Renderer;
        lost.Lose();
        rig.DamageAll();
        rig.Pump(() => rig.Driver.RendererRebuilds > 0);
        Assert.Same(rig.Driver.Renderer, pack.Capture.Renderer);
    }

    [Fact]
    public void A_failed_rebuild_stops_the_run_loop_with_a_failure()
    {
        SkipWithoutServer();
        using var rig = new LossRig(
            () => new RenderStack(new LosingRenderer(), null),
            () => throw new InvalidOperationException("no device"));
        var runLoop = new CompositorRunLoop(rig.Host, rig.Driver);
        Exception? failure = null;
        rig.Driver.RendererRecoveryFailed += error => failure = error;
        rig.Frame();
        var lost = (LosingRenderer)rig.Driver.Renderer;
        lost.Lose();
        rig.DamageAll();
        rig.Pump(() => failure is not null);

        Assert.NotNull(failure);
        Assert.Equal("no device", failure!.Message);
        Assert.Equal(0, rig.Driver.RendererRebuilds);
        Assert.False(runLoop.IsRunning);
        Assert.Equal(1, runLoop.ExitCode);
        Assert.Equal(HandoffStop.Failure, runLoop.Stopping);
        var rendered = rig.Driver.Views[0].Rendered;
        rig.DamageAll();
        rig.Pump(() => false, 100);
        Assert.Equal(rendered, rig.Driver.Views[0].Rendered);
    }

    [Fact]
    public void Without_a_factory_a_loss_is_a_failure()
    {
        SkipWithoutServer();
        using var rig = new LossRig(() => new RenderStack(new LosingRenderer(), null), factory: false);
        Exception? failure = null;
        rig.Driver.RendererRecoveryFailed += error => failure = error;
        rig.Frame();
        ((LosingRenderer)rig.Driver.Renderer).Lose();
        rig.DamageAll();
        rig.Pump(() => failure is not null);
        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public void Disposal_cancels_a_queued_rebuild()
    {
        SkipWithoutServer();
        var factories = 0;
        var rig = new LossRig(() => new RenderStack(new LosingRenderer(), null), () => factories++);
        rig.Frame();
        ((LosingRenderer)rig.Driver.Renderer).RaiseLost();
        rig.Driver.Dispose();
        rig.Host.Loop.Dispatch(0);
        Assert.Equal(0, factories);
        Assert.Equal(0, rig.Driver.RendererRebuilds);
        rig.Dispose();
    }

    [Fact]
    public void Gl_without_robustness_reads_the_loss_from_a_context_lost_error()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        SkipWithoutServer();
        using var rig = new LossRig(() => RendererCatalog.Create("gl", CompositorTestHost.RenderNodePath));
        var before = rig.Frame();
        var gl = (Basin.Render.Gl.GlRenderer)rig.Driver.Renderer;
        gl.SimulateErrorLoss();
        Assert.Equal(RendererLossDetection.Errors, gl.LossDetection);
        rig.DamageAll();
        rig.Pump(() => rig.Driver.RendererRebuilds > 0);
        Assert.Equal(1, rig.Driver.RendererRebuilds);
        AssertSameFrame(before, rig.Frame());
    }

    [Theory]
    [MemberData(nameof(GpuRenderers))]
    public void A_skia_chrome_surface_draws_again_after_the_rebuild(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        SkipWithoutServer();
        using var rig = new LossRig(() => RendererCatalog.Create(row, CompositorTestHost.RenderNodePath));
        rig.Frame();
        using var chrome = SkiaUIHosts.For(rig.Driver.Renderer);
        var bound = Assert.IsAssignableFrom<IRendererBound>(chrome);
        rig.Host.RendererReplaced += bound.OnRendererReplaced;
        var kind = (chrome.Produces & UITargetKind.Dmabuf) != 0 ? UITargetKind.Dmabuf : UITargetKind.Memory;
        using var surface = Assert.IsAssignableFrom<ISkiaUISurface>(
            chrome.CreateSurface(new UISurfaceOptions { Target = kind, Width = 16, Height = 16, Scale = 1.0 }));
        Paint(surface, SkiaSharp.SKColors.Red);

        Assert.True(LossInjection.TryInject(rig.Driver.Renderer));
        rig.DamageAll();
        rig.Pump(() => rig.Driver.RendererRebuilds > 0);
        Assert.Equal(1, rig.Driver.RendererRebuilds);

        Paint(surface, SkiaSharp.SKColors.Lime);
        Assert.True(surface.TryAcquire(out var frame));
        using (frame)
        {
            var drawn = frame.Buffer!;
            Assert.True(BufferCapture.TryReadRgba(drawn, rig.Driver.Renderer, out var rgba));
            var center = ((8 * drawn.Width) + 8) * 4;
            Assert.True(rgba[center + 1] > 200 && rgba[center] < 50 && rgba[center + 2] < 50, $"the chrome drew {rgba[center]},{rgba[center + 1]},{rgba[center + 2]} after the rebuild");
        }
    }

    [Fact]
    public void A_capture_session_gets_its_constraints_again_and_a_frame_after_the_device_changes()
    {
        using var host = new CompositorTestHost();
        using var sources = new Basin.Desktop.ImageCaptureSourceManager(host.Display);
        var formats = new DrmFormatSet();
        formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
        formats.Add(DrmFormat.Argb8888, DrmFormatSet.ModifierLinear);
        var constraints = new CaptureDmabufConstraints(formats, "/dev/null");
        using var manager = new Basin.Desktop.ImageCopyCaptureManager(
            host.Display, host.Buffers, new TestScreenCapture(host), constraints);

        Basin.Desktop.Protocol.ExtImageCopyCaptureManagerV1? capture = null;
        Basin.Desktop.Protocol.ExtOutputImageCaptureSourceManagerV1? outputs = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_image_copy_capture_manager_v1")
            {
                capture = registry.Bind<Basin.Desktop.Protocol.ExtImageCopyCaptureManagerV1>(e.Name, 1);
            }
            else if (e.Interface == "ext_output_image_capture_source_manager_v1")
            {
                outputs = registry.Bind<Basin.Desktop.Protocol.ExtOutputImageCaptureSourceManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var source = outputs!.CreateSource(host.Client.Outputs[0]);
        var session = capture!.CreateSession(source, 0);
        var done = 0;
        session.Done += (_, _) => done++;
        host.PumpUntil(() => done == 1);

        constraints.Offer(formats, "/dev/null");
        host.PumpUntil(() => done == 2);
        Assert.Equal(2, done);

        var frame = session.CreateFrame();
        var ready = false;
        var failed = false;
        frame.Ready += (_, _) => ready = true;
        frame.Failed += (_, _) => failed = true;
        var target = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0));
        frame.AttachBuffer(target.Proxy);
        frame.DamageBuffer(0, 0, 160, 120);
        frame.Capture();
        host.PumpUntil(() => ready || failed);
        Assert.True(ready);

        frame.Dispose();
        session.Dispose();
        source.Dispose();
        capture.Dispose();
        outputs.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void The_dmabuf_module_offers_the_capture_formats_of_the_new_renderer()
    {
        SkipWithoutServer();
        var formats = new DrmFormatSet();
        formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node: the dmabuf global needs a main device");
        var module = new LinuxDmabufModule(formats, CompositorTestHost.RenderNodePath, captureFormats: formats);
        using var rig = new LossRig(() => new RenderStack(new LosingRenderer(), null), services: services => services.Use(new ClientBufferRegistry()).Install(module));
        var constraints = rig.Host.Services!.Require<ICaptureDmabufConstraints>();
        var changed = 0;
        constraints.Changed += () => changed++;
        rig.Frame();
        ((LosingRenderer)rig.Driver.Renderer).Lose();
        rig.DamageAll();
        rig.Pump(() => rig.Driver.RendererRebuilds > 0);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void A_rebuild_clears_old_shaders_and_the_frame_filter()
    {
        SkipWithoutServer();
        using var rig = new LossRig(() => new RenderStack(new LosingRenderer(), null));
        var shaderNode = new SceneShader(rig.Scene.Root) { Shader = new CountingShader(), Bounds = new Box(0, 0, 8, 8) };
        var buffer = new SceneBuffer(rig.Scene.Root) { TextureShader = new CountingShader() };
        var output = rig.Driver.Views[0].Scene!;
        output.SetFrameFilter(new IdleFilter());
        rig.Frame();
        ((LosingRenderer)rig.Driver.Renderer).Lose();
        rig.DamageAll();
        rig.Pump(() => rig.Driver.RendererRebuilds > 0);

        Assert.Null(shaderNode.Shader);
        Assert.Null(buffer.TextureShader);
        Assert.Null(output.FrameFilter);
        rig.Frame();
    }

    [Fact]
    public void A_shader_fire_ends_when_a_rebuild_takes_its_shader()
    {
        using var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        _ = new SceneRect(window, 60, 40, new RenderColor(0.2f, 0.4f, 0.8f, 1f));
        var stack = new TransformStack(window);
        var shader = new CountingShader();
        var fire = new Basin.Effects.FireEffect { Shader = shader };
        fire.Begin(stack, hiding: true, new FrameTick(0, 16_666_667), 200_000_000);
        Assert.True(fire.Step(stack, new FrameTick(16_000_000, 16_666_667)));
        var before = shader.Calls;

        host.Scene.ReleaseRenderer(host.Renderer);
        shader.Dispose();

        Assert.False(fire.Step(stack, new FrameTick(32_000_000, 16_666_667)));
        Assert.False(fire.IsRunning);
        Assert.Equal(before, shader.Calls);
    }

    [Fact]
    public void The_services_dispose_what_they_own()
    {
        SkipWithoutServer();
        var owned = new CountingDisposable();
        using (var rig = new LossRig(() => new RenderStack(new LosingRenderer(), null)))
        {
            rig.Host.Services!.Own(owned);
            Assert.Equal(0, owned.Disposed);
        }

        Assert.Equal(1, owned.Disposed);
    }

    [Fact]
    public void A_ui_host_on_a_lost_device_does_not_stop_the_loop()
    {
        SkipWithoutServer();
        using var rig = new LossRig(() => new RenderStack(new LosingRenderer(), null));
        var lost = new LostUIHost();
        using var driver = new UIDriver(lost, rig.Host.Loop);
        driver.Pump();
        Assert.Equal(1, lost.Pumps);
    }

    [Fact]
    public void Waits_on_a_lost_vulkan_device_return_at_once()
    {
        CompositorTestHost.SkipUnlessRunnable("vulkan");
        var stack = RendererCatalog.Create("vulkan", CompositorTestHost.RenderNodePath);
        try
        {
            var device = ((Basin.Render.Vulkan.VulkanRenderer)stack.Renderer).Device;
            var raised = 0;
            device.Lost += () => raised++;
            device.MarkLost();
            device.MarkLost();
            var started = Environment.TickCount64;
            device.WaitFor(ulong.MaxValue / 2);
            Assert.True(device.IsComplete(ulong.MaxValue / 2));
            Assert.True(Environment.TickCount64 - started < 1000, "a wait on a lost device returned at once");
            Assert.Equal(1, raised);
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private static void Paint(ISkiaUISurface surface, SkiaSharp.SKColor color)
    {
        var canvas = surface.BeginDraw();
        canvas.Clear(color);
        surface.EndDraw();
    }

    private static Dictionary<string, int> Doubled(Dictionary<string, int> allowance) =>
        allowance.ToDictionary(pair => pair.Key, pair => pair.Value * 2);

    private static void AssertSameFrame(byte[] before, byte[] after)
    {
        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.True(Math.Abs(before[i] - after[i]) <= 2, $"byte {i} (pixel {i / 4 % Width},{i / 4 / Width}): before the loss {before[i]}, after {after[i]}");
        }
    }

    private static void SkipWithoutServer() =>
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");

    private sealed class LossRig : IDisposable
    {
        private readonly MemoryBuffer _content;
        private readonly RenderStack _first;
        private bool _disposed;

        public LossRig(
            Func<RenderStack> create,
            Action? counted = null,
            int outputs = 1,
            bool factory = true,
            Action<BasinServices>? services = null)
        {
            _first = create();
            Host = BasinHost.Create(HostOptions.ForBackend("headless"));
            var registry = Host.CreateServices();
            services?.Invoke(registry);
            registry.Freeze();
            Scene = new Scene.Scene();
            Layout = new OutputLayout();
            _content = SolidBuffer(32, 24, 0xFFFF0000);
            var node = new SceneBuffer(Scene.Root);
            node.SetBuffer(_content);
            node.SetPosition(8, 8);
            Driver = new OutputDriver(Host, Scene, Layout, _first.Renderer, _first.DeviceAllocator)
            {
                HeadlessMode = new OutputMode(Width, Height, 60_000),
                Requested = outputs,
                RendererFactory = factory
                    ? () =>
                    {
                        counted?.Invoke();
                        return create();
                    }
                    : null,
            };
            Driver.CreateInitialOutputs();
        }

        public BasinHost Host { get; }

        public Scene.Scene Scene { get; }

        public OutputLayout Layout { get; }

        public OutputDriver Driver { get; }

        public void DamageAll()
        {
            foreach (var view in Driver.Views)
            {
                view.Scene?.Ring.AddWhole();
                view.Scheduler?.ScheduleRepaint();
            }
        }

        public void Pump(Func<bool> until, int millis = 3000)
        {
            var deadline = Environment.TickCount64 + millis;
            while (!until() && Environment.TickCount64 < deadline)
            {
                Host.Loop.Dispatch(5);
            }
        }

        public byte[] Frame()
        {
            var view = Driver.Views[0];
            var output = (Basin.Backend.Headless.HeadlessOutput)view.Output;
            var start = view.Rendered;
            DamageAll();
            Pump(() => view.Rendered >= start + 1 && output.PresentedBuffer is not null);
            Assert.NotNull(output.PresentedBuffer);
            Assert.True(BufferCapture.TryReadRgba(output.PresentedBuffer!, Driver.Renderer, out var rgba), "the presented frame reads back");
            return rgba;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var renderer = Driver.Renderer;
            var allocator = Driver.DeviceAllocator;
            Driver.Dispose();
            Scene.Root.Destroy();
            _content.Destroy();
            Host.Services?.Dispose();
            Host.Dispose();
            allocator?.Dispose();
            renderer.Dispose();
        }

        private static MemoryBuffer SolidBuffer(int width, int height, uint pixel)
        {
            var buffer = new MemoryBuffer(width, height, DrmFormat.Argb8888);
            Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
            unsafe
            {
                for (var y = 0; y < height; y++)
                {
                    var row = (uint*)(view.Data + (y * view.Stride));
                    for (var x = 0; x < width; x++)
                    {
                        row[x] = pixel;
                    }
                }
            }

            buffer.EndDataAccess();
            return buffer;
        }
    }

    private sealed class CountingShader : IPixelShader
    {
        public int Calls { get; private set; }

        public void SetUniforms(ReadOnlySpan<PixelShaderUniformValue> values) => Calls++;

        public void Dispose()
        {
        }
    }

    private sealed class IdleFilter : IFrameFilter
    {
        public bool IsSupported => false;
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    private sealed class LostUIHost : IUIHost
    {
        public int Pumps { get; private set; }

        public UITargetKind Produces => UITargetKind.Memory;

        public long? NextDueMillis => null;

        public event Action? WakeupRequested
        {
            add
            {
            }

            remove
            {
            }
        }

        public IUISurface? CreateSurface(in UISurfaceOptions options) => null;

        public void Pump()
        {
            Pumps++;
            throw new RendererLostException("injected");
        }

        public void Dispose()
        {
        }
    }

    private sealed class BoundProbe : IRendererBound
    {
        public int Calls { get; private set; }

        public bool OldWasDisposed { get; private set; }

        public void OnRendererReplaced(RendererReplacement replacement)
        {
            Calls++;
            OldWasDisposed = replacement.Old.Renderer is LosingRenderer { Disposed: true };
        }
    }
}

internal static class LossInjection
{
    public static bool TryInject(IRenderer renderer)
    {
        switch (renderer)
        {
            case Basin.Render.Gl.GlRenderer gl:
                gl.SimulateLoss();
                return true;
            case Basin.Render.Vulkan.VulkanRenderer vulkan:
                vulkan.SimulateLoss();
                return true;
            case Basin.Render.Skia.SkiaGlRenderer skiaGl:
                skiaGl.SimulateLoss();
                return true;
            case Basin.Render.Skia.SkiaVulkanRenderer skiaVulkan:
                skiaVulkan.SimulateLoss();
                return true;
            case Basin.Render.Skia.SkiaGraphiteRenderer graphite:
                graphite.SimulateLoss();
                return true;
            case LosingRenderer losing:
                losing.Lose();
                return true;
            default:
                return false;
        }
    }
}

internal sealed class LosingRenderer : IRenderer
{
    private readonly Basin.Render.Pixman.PixmanRenderer _inner = new();
    private Action? _deviceLost;
    private bool _lost;

    public event Action? DeviceLost
    {
        add => _deviceLost += value;
        remove => _deviceLost -= value;
    }

    public int Raised { get; private set; }

    public int Throws { get; private set; }

    public bool Disposed { get; private set; }

    public RendererLossDetection LossDetection => RendererLossDetection.DeviceStatus;

    public IReadOnlyList<DrmFormat> ShmTextureFormats => _inner.ShmTextureFormats;

    public ColorTransformCapability ColorTransform => _inner.ColorTransform;

    public void Lose() => _lost = true;

    public void RaiseLost()
    {
        _lost = true;
        Raised++;
        _deviceLost?.Invoke();
    }

    public ITexture? ImportTexture(IBuffer buffer) => _inner.ImportTexture(buffer);

    public IColorLut? ImportLut(ColorLut3D lut) => _inner.ImportLut(lut);

    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        if (_lost)
        {
            Throws++;
            if (Raised == 0)
            {
                Raised++;
                _deviceLost?.Invoke();
            }

            throw new RendererLostException("injected");
        }

        return _inner.BeginBufferPass(target, options);
    }

    public void Dispose()
    {
        Disposed = true;
        _inner.Dispose();
    }
}
