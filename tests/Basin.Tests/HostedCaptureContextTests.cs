using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Skia;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Hosted;
using SkiaSharp;
using Xunit;
using AvaloniaEglDisplay = Avalonia.OpenGL.Egl.EglDisplay;

namespace Basin.Tests;

public sealed class HostedCaptureContextTests
{
    private const int Width = 160;
    private const int Height = 120;
    private const uint Fill = 0xFF3366AA;

    private static readonly RenderColor Background = new(0f, 0f, 0f, 1f);

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern nint mmap(nint addr, nuint length, int prot, int flags, int fd, nint offset);

    [DllImport("libc")]
    private static extern int munmap(nint addr, nuint length);

    [DllImport("libEGL.so.1")]
    private static extern nint eglGetCurrentContext();

    private sealed class CapturingLog : IBasinLogSink, IDisposable
    {
        private readonly IBasinLogSink? _previous = BasinLog.Sink;
        private readonly BasinLogLevel _previousLevel = BasinLog.Level;

        public CapturingLog()
        {
            BasinLog.Sink = this;
            BasinLog.Level = BasinLogLevel.Info;
        }

        public List<(BasinLogLevel Level, string Message)> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            Lines.Add((level, message.ToString()));

        public void Dispose()
        {
            BasinLog.Sink = _previous;
            BasinLog.Level = _previousLevel;
        }
    }

    private sealed class RefusingBinding : IHostedContextBinding
    {
        public int Attempts { get; private set; }

        public bool Enter()
        {
            Attempts++;
            return false;
        }

        public void Exit() => throw new InvalidOperationException("Exit must not run after a refused Enter.");
    }

    private sealed class FakeLease(SKCanvas canvas, GRContext context, IPlatformGraphicsContext platform) : ISkiaSharpApiLease
    {
        public SKCanvas SkCanvas => canvas;

        public GRContext? GrContext => context;

        public SKSurface? SkSurface => null;

        public double CurrentOpacity => 1.0;

        public ISkiaSharpPlatformGraphicsApiLease? TryLeasePlatformGraphicsApi() => new PlatformLease(platform);

        public void Dispose()
        {
        }
    }

    private sealed class PlatformLease(IPlatformGraphicsContext context) : ISkiaSharpPlatformGraphicsApiLease
    {
        public IPlatformGraphicsContext Context => context;

        public void Dispose()
        {
        }
    }

    [Fact]
    public void A_dmabuf_from_a_toolkit_frame_captures_outside_the_frame_on_the_toolkit_context()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        BasinCounters.Reset();
        var other = HostedGpuContext.Create(CompositorTestHost.RenderNodePath);
        var otherContext = eglGetCurrentContext();
        Assert.NotEqual(0, otherContext);

        var nodeFd = open(CompositorTestHost.RenderNodePath, 2 | 0x80000);
        Assert.True(nodeFd >= 0);
        var gbm = Mesa.Gbm.GbmDevice.Create(nodeFd);
        var mesaDisplay = Mesa.Egl.EglDisplay.GetPlatformDisplay(gbm);
        var display = new AvaloniaEglDisplay(mesaDisplay.Handle, new global::Avalonia.OpenGL.Egl.EglDisplayOptions());
        var toolkit = display.CreateContext(null);
        GRContext grContext;
        using (toolkit.MakeCurrent())
        {
            var glInterface = GRGlInterface.CreateGles(name => toolkit.GlInterface.GetProcAddress(name));
            Assert.NotNull(glInterface);
            grContext = GRContext.CreateGl(glInterface);
            Assert.NotNull(grContext);
            glInterface.Dispose();
        }

        Assert.Equal(otherContext, eglGetCurrentContext());
        var source = CreateLinearDmabuf(gbm);
        var scene = new Scene.Scene();
        var content = new Scene.SceneBuffer(scene.Root);
        content.SetPosition(20, 10);
        content.SetBuffer(source);
        var renderer = new HostedRenderer();
        var frameTarget = new HostedFrameTarget(Width, Height);
        try
        {
            using (toolkit.MakeCurrent())
            {
                using var surface = SKSurface.Create(grContext, false, new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                Assert.NotNull(surface);
                using var lease = new FakeLease(surface.Canvas, grContext, toolkit);
                Assert.True(renderer.BindFrame(lease));
                try
                {
                    Assert.True(scene.Render(renderer, frameTarget, Background));
                }
                finally
                {
                    renderer.UnbindFrame();
                }

                surface.Flush();
            }

            Assert.NotNull(renderer.EglImport);
            Assert.Same(grContext, renderer.OffscreenContext);
            Assert.NotNull(renderer.OffscreenBinding);
            Assert.Equal(otherContext, eglGetCurrentContext());

            for (var capture = 0; capture < 2; capture++)
            {
                var target = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
                try
                {
                    Assert.True(scene.Render(renderer, target, Background));
                    Assert.Equal(otherContext, eglGetCurrentContext());
                    Assert.True(Count(target, 0xAA, 0x66, 0x33) >= 120 * 90, $"capture {capture} lost the dmabuf pixels");
                }
                finally
                {
                    target.Destroy();
                }
            }

            scene.Root.Destroy();
            renderer.Dispose();
            Assert.Equal(otherContext, eglGetCurrentContext());
        }
        finally
        {
            frameTarget.Destroy();
            source.Destroy();
            using (toolkit.MakeCurrent())
            {
                grContext.AbandonContext(releaseResources: true);
                grContext.Dispose();
            }

            toolkit.Dispose();
            display.Dispose();
            mesaDisplay.Dispose();
            gbm.Dispose();
            _ = close(nodeFd);
            other.Dispose();
        }

        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    private sealed class Toolkit : IDisposable
    {
        private readonly int _fd;
        private readonly Mesa.Gbm.GbmDevice _gbm;
        private readonly Mesa.Egl.EglDisplay _mesaDisplay;

        public Toolkit()
        {
            _fd = open(CompositorTestHost.RenderNodePath, 2 | 0x80000);
            Assert.True(_fd >= 0);
            _gbm = Mesa.Gbm.GbmDevice.Create(_fd);
            _mesaDisplay = Mesa.Egl.EglDisplay.GetPlatformDisplay(_gbm);
            Display = new AvaloniaEglDisplay(_mesaDisplay.Handle, new global::Avalonia.OpenGL.Egl.EglDisplayOptions());
            Context = Display.CreateContext(null);
            using (Context.MakeCurrent())
            {
                var glInterface = GRGlInterface.CreateGles(name => Context.GlInterface.GetProcAddress(name));
                Assert.NotNull(glInterface);
                Gr = GRContext.CreateGl(glInterface);
                Assert.NotNull(Gr);
                glInterface.Dispose();
            }
        }

        public AvaloniaEglDisplay Display { get; }

        public global::Avalonia.OpenGL.Egl.EglContext Context { get; }

        public GRContext Gr { get; }

        public int Frame(HostedRenderer renderer, Scene.Scene scene, HostedFrameTarget target)
        {
            using (Context.MakeCurrent())
            {
                var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(Gr, false, info);
                Assert.NotNull(surface);
                using var lease = new FakeLease(surface.Canvas, Gr, Context);
                Assert.True(renderer.BindFrame(lease));
                try
                {
                    Assert.True(scene.Render(renderer, target, Background));
                }
                finally
                {
                    renderer.UnbindFrame();
                }

                surface.Flush();
                var shown = new MemoryBuffer(Width, Height, DrmFormat.Argb8888);
                Assert.True(shown.BeginDataAccess(BufferDataAccess.Write, out var view));
                Assert.True(surface.ReadPixels(info, view.Data, view.Stride, 0, 0));
                shown.EndDataAccess();
                var count = Count(shown, 0xAA, 0x66, 0x33);
                shown.Destroy();
                return count;
            }
        }

        public void Dispose()
        {
            using (Context.MakeCurrent())
            {
                Gr.AbandonContext(releaseResources: true);
                Gr.Dispose();
            }

            Context.Dispose();
            Display.Dispose();
            _mesaDisplay.Dispose();
            _gbm.Dispose();
            _ = close(_fd);
        }
    }

    [Fact]
    public void A_new_toolkit_EGL_display_replaces_the_import_and_draws_the_dmabuf_again()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        BasinCounters.Reset();
        var other = HostedGpuContext.Create(CompositorTestHost.RenderNodePath);
        var otherContext = eglGetCurrentContext();
        var nodeFd = open(CompositorTestHost.RenderNodePath, 2 | 0x80000);
        Assert.True(nodeFd >= 0);
        var gbm = Mesa.Gbm.GbmDevice.Create(nodeFd);
        var source = CreateLinearDmabuf(gbm);
        var scene = new Scene.Scene();
        var content = new Scene.SceneBuffer(scene.Root);
        content.SetPosition(20, 10);
        content.SetBuffer(source);
        var renderer = new HostedRenderer();
        var frameTarget = new HostedFrameTarget(Width, Height);
        var first = new Toolkit();
        Toolkit? second = null;
        try
        {
            Assert.Equal(120 * 90, first.Frame(renderer, scene, frameTarget));
            var firstImport = renderer.EglImport;
            Assert.NotNull(firstImport);
            Assert.Equal(first.Display.Handle, firstImport.DisplayHandle);
            Assert.Equal(120 * 90, Capture(renderer, scene));
            first.Dispose();

            second = new Toolkit();
            Assert.NotEqual(first.Display.Handle, second.Display.Handle);
            using (var log = new CapturingLog())
            {
                Assert.Equal(120 * 90, second.Frame(renderer, scene, frameTarget));
                Assert.Single(log.Lines, line => line.Level == BasinLogLevel.Info && line.Message.Contains("EGL display changed", StringComparison.Ordinal));
            }

            Assert.NotSame(firstImport, renderer.EglImport);
            Assert.Equal(second.Display.Handle, renderer.EglImport!.DisplayHandle);
            Assert.Same(second.Gr, renderer.OffscreenContext);
            Assert.Equal(120 * 90, Capture(renderer, scene));
            Assert.Equal(otherContext, eglGetCurrentContext());

            scene.Root.Destroy();
            renderer.Dispose();
        }
        finally
        {
            frameTarget.Destroy();
            source.Destroy();
            second?.Dispose();
            gbm.Dispose();
            _ = close(nodeFd);
            other.Dispose();
        }

        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void A_dmabuf_imported_before_the_first_frame_waits_and_draws_on_the_toolkit_context()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        BasinCounters.Reset();
        var nodeFd = open(CompositorTestHost.RenderNodePath, 2 | 0x80000);
        Assert.True(nodeFd >= 0);
        var gbm = Mesa.Gbm.GbmDevice.Create(nodeFd);
        var source = CreateLinearDmabuf(gbm);
        var scene = new Scene.Scene();
        var content = new Scene.SceneBuffer(scene.Root);
        content.SetPosition(20, 10);
        content.SetBuffer(source);
        var renderer = new HostedRenderer();
        var frameTarget = new HostedFrameTarget(Width, Height);
        var toolkit = new Toolkit();
        try
        {
            Assert.True(renderer.TryEnableEgl(toolkit.Display.Handle));
            Assert.Equal(0, Capture(renderer, scene));
            Assert.Equal(120 * 90, toolkit.Frame(renderer, scene, frameTarget));
            Assert.Equal(120 * 90, Capture(renderer, scene));
            scene.Root.Destroy();
            renderer.Dispose();
        }
        finally
        {
            frameTarget.Destroy();
            source.Destroy();
            toolkit.Dispose();
            gbm.Dispose();
            _ = close(nodeFd);
        }

        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    private static int Capture(HostedRenderer renderer, Scene.Scene scene)
    {
        var target = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(scene.Render(renderer, target, Background));
            return Count(target, 0xAA, 0x66, 0x33);
        }
        finally
        {
            target.Destroy();
        }
    }

    [Fact]
    public void A_binding_that_cannot_enter_captures_on_the_raster_row_with_one_warning()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        BasinCounters.Reset();
        var gpu = HostedGpuContext.Create(CompositorTestHost.RenderNodePath);
        var source = new MemoryBuffer(64, 48, DrmFormat.Xrgb8888);
        Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < 48; y++)
            {
                new Span<uint>((byte*)view.Data + (y * view.Stride), 64).Fill(Fill);
            }
        }

        source.EndDataAccess();
        var scene = new Scene.Scene();
        var content = new Scene.SceneBuffer(scene.Root);
        content.SetBuffer(source);
        var renderer = new HostedRenderer();
        var binding = new RefusingBinding();
        renderer.UseOffscreenContext(gpu.Context, binding);
        using (var log = new CapturingLog())
        {
            for (var capture = 0; capture < 2; capture++)
            {
                var target = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
                Assert.True(scene.Render(renderer, target, Background));
                Assert.Equal(64 * 48, Count(target, 0xAA, 0x66, 0x33));
                target.Destroy();
            }

            Assert.Equal(2, binding.Attempts);
            Assert.Single(log.Lines, line => line.Level == BasinLogLevel.Warn && line.Message.Contains("raster row", StringComparison.Ordinal));
        }

        scene.Root.Destroy();
        renderer.Dispose();
        source.Destroy();
        gpu.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void A_gpu_pass_outside_a_frame_starts_from_the_pixels_of_its_target()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        BasinCounters.Reset();
        var gpu = HostedGpuContext.Create(CompositorTestHost.RenderNodePath);
        var renderer = new HostedRenderer();
        renderer.UseOffscreenContext(gpu.Context);
        var filled = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        Assert.True(filled.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < Height; y++)
            {
                new Span<uint>((byte*)view.Data + (y * view.Stride), Width).Fill(Fill);
            }
        }

        filled.EndDataAccess();
        var pass = renderer.BeginBufferPass(filled, default);
        pass.AddRect(new RenderColor(1f, 1f, 1f, 1f), new Box(0, 0, 10, 10));
        Assert.True(pass.Submit());
        Assert.Equal((Width * Height) - 100, Count(filled, 0xAA, 0x66, 0x33));

        var empty = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        Assert.True(renderer.BeginBufferPass(empty, default).Submit());
        Assert.Equal(0, Count(empty, 0xAA, 0x66, 0x33));

        filled.Destroy();
        empty.Destroy();
        renderer.Dispose();
        gpu.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void A_host_that_requires_gpu_captures_offers_dmabuf_only_with_an_offscreen_context()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        using var gpu = HostedGpuContext.Create(CompositorTestHost.RenderNodePath);
        var options = new BasinCompositorOptions
        {
            AppName = "basin-tests",
            DmabufRequiresGpuCapture = true,
            ConfigureServices = HostedDrivers.Without,
        };

        using (var withheld = new BasinCompositorHost(options))
        {
            Assert.True(withheld.Renderer.TryEnableEgl(gpu.EglDisplayHandle));
            Assert.Null(withheld.Dmabuf);
        }

        using var offered = new BasinCompositorHost(options);
        offered.Renderer.UseOffscreenContext(gpu.Context);
        Assert.True(offered.Renderer.TryEnableEgl(gpu.EglDisplayHandle));
        Assert.NotNull(offered.Dmabuf);
    }

    [Fact]
    public void A_new_EGL_display_on_the_same_device_keeps_the_advertised_dmabuf_global()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        using var first = HostedGpuContext.Create(CompositorTestHost.RenderNodePath);
        using var host = new BasinCompositorHost(new BasinCompositorOptions
        {
            AppName = "basin-tests",
            ConfigureServices = HostedDrivers.Without,
        });
        Assert.True(host.Renderer.TryEnableEgl(first.EglDisplayHandle));
        var advertised = host.Dmabuf;
        Assert.NotNull(advertised);

        using var second = HostedGpuContext.Create(CompositorTestHost.RenderNodePath);
        Assert.NotEqual(first.EglDisplayHandle, second.EglDisplayHandle);
        Assert.True(host.Renderer.TryEnableEgl(second.EglDisplayHandle));
        Assert.Equal(second.EglDisplayHandle, host.Renderer.EglImport!.DisplayHandle);
        Assert.Same(advertised, host.Dmabuf);
    }

    private static unsafe DmabufBuffer CreateLinearDmabuf(Mesa.Gbm.GbmDevice gbm)
    {
        using var bo = gbm.CreateBuffer(
            120, 90, (uint)DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], Mesa.Gbm.GbmBufferFlags.Rendering);
        var attributes = new DmabufAttributes
        {
            Width = 120,
            Height = 90,
            Format = DrmFormat.Xrgb8888,
            Modifier = bo.Modifier,
            PlaneCount = 1,
        };
        attributes.Fds[0] = bo.ExportDmaBufForPlane(0);
        attributes.Offsets[0] = bo.GetOffsetForPlane(0);
        attributes.Strides[0] = bo.GetStrideForPlane(0);
        var size = (nuint)(attributes.Offsets[0] + (attributes.Strides[0] * 90));
        var map = mmap(0, size, 3, 1, attributes.Fds[0], 0);
        Assert.NotEqual(-1, map);
        for (var y = 0; y < 90; y++)
        {
            new Span<uint>((byte*)map + attributes.Offsets[0] + (y * attributes.Strides[0]), 120).Fill(Fill);
        }

        _ = munmap(map, size);
        return new DmabufBuffer(attributes);
    }

    private static int Count(MemoryBuffer target, byte b, byte g, byte r)
    {
        Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
        var count = 0;
        unsafe
        {
            for (var y = 0; y < target.Height; y++)
            {
                var row = new ReadOnlySpan<byte>((byte*)view.Data + (y * view.Stride), target.Width * 4);
                for (var x = 0; x < target.Width; x++)
                {
                    if (row[x * 4] == b && row[(x * 4) + 1] == g && row[(x * 4) + 2] == r)
                    {
                        count++;
                    }
                }
            }
        }

        target.EndDataAccess();
        return count;
    }
}
