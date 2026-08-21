using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;
using Basin.Diagnostics;
using Basin.Render.Avalonia;
using Basin.Render.Skia;
using SkiaSharp;
using Xunit;

namespace Basin.Tests;

public sealed class AvaloniaRendererTests
{
    private const int Width = 160;
    private const int Height = 120;

    private static readonly RenderColor Background = new(0.1f, 0.2f, 0.3f, 1f);

    private static (Scene.Scene Scene, MemoryBuffer Gradient) BuildScene()
    {
        var scene = new Scene.Scene();
        var rect = new Scene.SceneRect(scene.Root, 60, 40, new RenderColor(0.9f, 0.4f, 0.1f, 1f));
        rect.SetPosition(8, 6);
        var translucent = new Scene.SceneRect(scene.Root, 50, 50, new RenderColor(0.1f, 0.7f, 0.3f, 0.5f));
        translucent.SetPosition(30, 26);
        var gradient = new MemoryBuffer(64, 48, DrmFormat.Xrgb8888);
        if (gradient.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            Fill.Gradient(64, 48)(view.Data, view.Stride);
            gradient.EndDataAccess();
        }

        var content = new Scene.SceneBuffer(scene.Root);
        content.SetPosition(80, 50);
        content.SetBuffer(gradient);
        return (scene, gradient);
    }

    private static byte[] RenderReference()
    {
        var (scene, gradient) = BuildScene();
        using var renderer = new SkiaRenderer();
        var target = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        Assert.True(scene.Render(renderer, target, Background));
        var pixels = new byte[Width * Height * 4];
        Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
        unsafe
        {
            for (var y = 0; y < Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(view.Data + y * view.Stride, pixels, y * Width * 4, Width * 4);
            }
        }

        target.EndDataAccess();
        scene.Root.Destroy();
        target.Destroy();
        gradient.Destroy();
        return pixels;
    }

    [Fact]
    public void Canvas_pass_matches_the_skia_row()
    {
        BasinCounters.Reset();
        var reference = RenderReference();

        var (scene, gradient) = BuildScene();
        var renderer = new AvaloniaRenderer();
        var target = new AvaloniaFrameTarget(Width, Height);
        var actual = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        Assert.True(actual.BeginDataAccess(BufferDataAccess.Read | BufferDataAccess.Write, out var view));
        Assert.True(SkiaRenderer.TryImageInfo(Width, Height, DrmFormat.Xrgb8888, out var info));
        using (var surface = SKSurface.Create(info, view.Data, view.Stride))
        {
            Assert.True(renderer.BindFrame(surface.Canvas, context: null));
            Assert.True(scene.Render(renderer, target, Background));
            renderer.UnbindFrame();
            surface.Flush();
        }

        var pixels = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(view.Data + y * view.Stride, pixels, y * Width * 4, Width * 4);
        }

        actual.EndDataAccess();
        Assert.Equal(reference, pixels);

        scene.Root.Destroy();
        renderer.Dispose();
        target.Destroy();
        actual.Destroy();
        gradient.Destroy();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [AvaloniaFact]
    public void Lease_frame_matches_the_skia_row()
    {
        var reference = RenderReference();
        var (scene, gradient) = BuildScene();
        var renderer = new AvaloniaRenderer();
        var target = new AvaloniaFrameTarget(Width, Height);
        var sawLease = false;
        var host = new SceneHost(context =>
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null)
            {
                return;
            }

            using var lease = feature.Lease();
            if (!renderer.BindFrame(lease))
            {
                return;
            }

            try
            {
                sawLease = true;
                scene.Render(renderer, target, Background);
            }
            finally
            {
                renderer.UnbindFrame();
            }
        });
        var window = new Window
        {
            Width = Width,
            Height = Height,
            CanResize = false,
            Content = host,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.True(sawLease);
        Assert.NotNull(frame);

        var rgba = new byte[Width * Height * 4];
        using (var locked = frame.Lock())
        {
            Assert.Equal(Width, frame.PixelSize.Width);
            Assert.Equal(Height, frame.PixelSize.Height);
            for (var y = 0; y < Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(locked.Address + y * locked.RowBytes, rgba, y * Width * 4, Width * 4);
            }
        }

        window.Close();
        Dispatcher.UIThread.RunJobs();
        scene.Root.Destroy();
        renderer.Dispose();
        target.Destroy();
        gradient.Destroy();

        for (var i = 0; i < Width * Height; i++)
        {
            var b = reference[i * 4];
            var g = reference[i * 4 + 1];
            var r = reference[i * 4 + 2];
            Assert.True(
                r == rgba[i * 4] && g == rgba[i * 4 + 1] && b == rgba[i * 4 + 2],
                $"pixel {i % Width},{i / Width}: expected {r:X2}{g:X2}{b:X2}, rgba {rgba[i * 4]:X2}{rgba[i * 4 + 1]:X2}{rgba[i * 4 + 2]:X2}");
        }
    }

    [Fact]
    public void A_foreign_target_is_refused()
    {
        BasinCounters.Reset();
        var renderer = new AvaloniaRenderer();
        var target = new MemoryBuffer(8, 8, DrmFormat.Xrgb8888);
        Assert.Throws<InvalidOperationException>(() => renderer.BeginBufferPass(target, new RenderPassOptions()));
        renderer.Dispose();
        target.Destroy();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void An_unbound_frame_is_refused()
    {
        BasinCounters.Reset();
        var renderer = new AvaloniaRenderer();
        var target = new AvaloniaFrameTarget(8, 8);
        Assert.Throws<InvalidOperationException>(() => renderer.BeginBufferPass(target, new RenderPassOptions()));
        renderer.Dispose();
        target.Destroy();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void The_frame_target_carries_no_pixels()
    {
        BasinCounters.Reset();
        var target = new AvaloniaFrameTarget(32, 16, 2.0);
        Assert.Equal(32, target.Width);
        Assert.Equal(16, target.Height);
        Assert.Equal(2.0, target.Scale);
        Assert.False(target.BeginDataAccess(BufferDataAccess.Read, out _));
        target.Destroy();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void A_lost_context_answers_no_textures_until_the_next_bind()
    {
        BasinCounters.Reset();
        var renderer = new AvaloniaRenderer();
        var buffer = new MemoryBuffer(16, 16, DrmFormat.Argb8888);
        renderer.NotifyContextLost();
        Assert.True(renderer.IsContextLost);
        Assert.Null(renderer.ImportTexture(buffer));

        using var surface = SKSurface.Create(new SKImageInfo(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul));
        Assert.True(renderer.BindFrame(surface.Canvas, context: null));
        Assert.False(renderer.IsContextLost);
        var texture = renderer.ImportTexture(buffer);
        Assert.NotNull(texture);
        renderer.UnbindFrame();
        texture.Dispose();
        renderer.Dispose();
        buffer.Destroy();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void Frame_loop_allocates_nothing_over_1000_frames()
    {
        BasinCounters.Reset();
        var (scene, gradient) = BuildScene();
        using var renderer = new AvaloniaRenderer();
        var target = new AvaloniaFrameTarget(Width, Height);
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));

        void Frame()
        {
            Assert.True(renderer.BindFrame(surface.Canvas, context: null));
            scene.Render(renderer, target, Background);
            renderer.UnbindFrame();
        }

        for (var i = 0; i < 20; i++)
        {
            Frame();
        }

        var first = MeasurePass(1000, Frame);
        if (first != 0)
        {
            Assert.Equal(0, MeasurePass(1000, Frame));
        }

        scene.Root.Destroy();
        target.Destroy();
        gradient.Destroy();
    }

    private static long MeasurePass(int rounds, Action round)
    {
        var allocated = 0L;
        for (var i = 0; i < rounds; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            round();
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return allocated;
    }

    private sealed class SceneVisualHandler : CompositionCustomVisualHandler
    {
        private readonly Action<ImmediateDrawingContext> _render;

        public SceneVisualHandler(Action<ImmediateDrawingContext> render) => _render = render;

        public override void OnRender(ImmediateDrawingContext context) => _render(context);
    }

    private sealed class SceneHost : Control
    {
        private readonly Action<ImmediateDrawingContext> _render;
        private CompositionCustomVisual? _visual;

        public SceneHost(Action<ImmediateDrawingContext> render) => _render = render;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            var element = ElementComposition.GetElementVisual(this);
            if (element is null)
            {
                return;
            }

            _visual = element.Compositor.CreateCustomVisual(new SceneVisualHandler(_render));
            ElementComposition.SetElementChildVisual(this, _visual);
            _visual.Size = new(Bounds.Width, Bounds.Height);
        }

        protected override void ArrangeCore(Rect finalRect)
        {
            base.ArrangeCore(finalRect);
            if (_visual is not null)
            {
                _visual.Size = new(Bounds.Width, Bounds.Height);
            }
        }
    }
}
