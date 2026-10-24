using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class UIHostDmabufTests
{
    private const uint Drawn = 0xff3060c0;

    [Fact]
    public void A_vulkan_host_dmabuf_carries_the_pixels_it_was_given()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new Basin.Render.Vulkan.VulkanRenderer(CompositorTestHost.RenderNodePath);
        using var allocator = renderer.Device.CreateAllocator();
        using var host = new Basin.UI.Skia.SkiaVulkanUIHost(renderer.Device, null, allocator);

        Assert.Equal(UITargetKind.Dmabuf, host.Produces);
        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 64,
            Height = 64,
            Scale = 1,
        });
        Assert.NotNull(surface);
        using (surface)
        {
            var skia = (Basin.UI.Skia.ISkiaUISurface)surface;
            skia.BeginDraw().Clear(new SkiaSharp.SKColor(0x30, 0x60, 0xc0, 0xff));
            skia.EndDraw();

            Assert.True(surface.TryAcquire(out var frame));
            using (frame)
            {
                Assert.True(frame.Buffer!.TryGetDmabuf(out _), "the host promised a dmabuf");

                var target = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
                var texture = renderer.ImportTexture(frame.Buffer);
                Assert.NotNull(texture);
                using (texture)
                {
                    var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
                    pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(0, 0, 64, 64) });
                    Assert.True(pass.Submit());
                }

                Assert.Equal(Drawn, PixelAt(target, 32, 32));
                target.Destroy();
            }
        }
    }

    [Fact]
    public void A_redraw_at_the_same_size_reaches_the_buffer_that_is_handed_over()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new Basin.Render.Vulkan.VulkanRenderer(CompositorTestHost.RenderNodePath);
        using var allocator = renderer.Device.CreateAllocator();
        using var host = new Basin.UI.Skia.SkiaVulkanUIHost(renderer.Device, null, allocator);

        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 64,
            Height = 64,
            Scale = 1,
        });
        Assert.NotNull(surface);
        using (surface)
        {
            var skia = (Basin.UI.Skia.ISkiaUISurface)surface;

            skia.BeginDraw().Clear(new SkiaSharp.SKColor(0xff, 0x00, 0x00, 0xff));
            skia.EndDraw();
            Assert.True(surface.TryAcquire(out var first));

            Assert.True(surface.Configure(64, 64, 1));
            skia.BeginDraw().Clear(new SkiaSharp.SKColor(0x30, 0x60, 0xc0, 0xff));
            skia.EndDraw();
            first.Dispose();

            Assert.True(surface.TryAcquire(out var second));
            using (second)
            {
                Assert.Equal(Drawn, Sample(renderer, second.Buffer!));
            }
        }
    }

    [Fact]
    public void A_graphite_host_dmabuf_carries_the_pixels_it_was_given()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        Assert.SkipWhen(
            !SkiaSharp.SKGraphiteContext.IsBackendAvailable(SkiaSharp.SKGraphiteBackend.Vulkan),
            "no Graphite Vulkan backend");

        using var graphite = new Basin.Render.Skia.SkiaGraphiteRenderer(CompositorTestHost.RenderNodePath);
        using var renderer = new Basin.Render.Vulkan.VulkanRenderer(CompositorTestHost.RenderNodePath);
        using var allocator = graphite.Device.CreateAllocator();
        using var host = new Basin.UI.Skia.SkiaGraphiteUIHost(graphite.Device, graphite.Context, graphite.Recorder, allocator);

        Assert.Equal(UITargetKind.Dmabuf, host.Produces);
        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 64,
            Height = 64,
            Scale = 1,
        });
        Assert.NotNull(surface);
        using (surface)
        {
            var skia = (Basin.UI.Skia.ISkiaUISurface)surface;

            skia.BeginDraw().Clear(new SkiaSharp.SKColor(0xff, 0x00, 0x00, 0xff));
            skia.EndDraw();
            Assert.True(surface.TryAcquire(out var first));

            Assert.True(surface.Configure(64, 64, 1));
            skia.BeginDraw().Clear(new SkiaSharp.SKColor(0x30, 0x60, 0xc0, 0xff));
            skia.EndDraw();
            first.Dispose();

            Assert.True(surface.TryAcquire(out var second));
            using (second)
            {
                Assert.True(second.Buffer!.TryGetDmabuf(out _), "the host promised a dmabuf");
                Assert.Equal(Drawn, Sample(renderer, second.Buffer));
            }
        }
    }

    [Fact]
    public void A_failed_resize_keeps_the_last_buffer()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new Basin.Render.Vulkan.VulkanRenderer(CompositorTestHost.RenderNodePath);
        using var gbm = renderer.Device.CreateAllocator();
        using var allocator = new FirstOnlyAllocator(gbm);
        using var host = new Basin.UI.Skia.SkiaVulkanUIHost(renderer.Device, null, allocator);

        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 64,
            Height = 64,
            Scale = 1,
        });
        Assert.NotNull(surface);
        using (surface)
        {
            var skia = (Basin.UI.Skia.ISkiaUISurface)surface;
            skia.BeginDraw().Clear(new SkiaSharp.SKColor(0xff, 0x00, 0x00, 0xff));
            skia.EndDraw();

            Assert.False(surface.Configure(128, 128, 1));
            Assert.Equal(64, surface.Size.Width);

            skia.BeginDraw().Clear(new SkiaSharp.SKColor(0x30, 0x60, 0xc0, 0xff));
            skia.EndDraw();
            Assert.True(surface.TryAcquire(out var second));
            using (second)
            {
                var buffer = Assert.IsAssignableFrom<BufferBase>(second.Buffer);
                Assert.False(buffer.IsDestroyed);
                Assert.Equal(64, buffer.Width);
                Assert.Equal(Drawn, Sample(renderer, buffer));
            }
        }
    }

    [Fact]
    public void A_buffer_the_device_cannot_import_is_read_back()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new Basin.Render.Vulkan.VulkanRenderer(CompositorTestHost.RenderNodePath);
        using var allocator = new MappedAllocator();
        using var host = new Basin.UI.Skia.SkiaVulkanUIHost(renderer.Device, null, allocator);
        AssertReadBack(host);
    }

    [Fact]
    public void A_buffer_the_graphite_device_cannot_import_is_read_back()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        Assert.SkipWhen(
            !SkiaSharp.SKGraphiteContext.IsBackendAvailable(SkiaSharp.SKGraphiteBackend.Vulkan),
            "no Graphite Vulkan backend");

        using var graphite = new Basin.Render.Skia.SkiaGraphiteRenderer(CompositorTestHost.RenderNodePath);
        using var allocator = new MappedAllocator();
        using var host = new Basin.UI.Skia.SkiaGraphiteUIHost(graphite.Device, graphite.Context, graphite.Recorder, allocator);
        AssertReadBack(host);
    }

    private static void AssertReadBack(IUIHost host)
    {
        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 64,
            Height = 64,
            Scale = 1,
        });
        Assert.NotNull(surface);
        using (surface)
        {
            var skia = (Basin.UI.Skia.ISkiaUISurface)surface;
            skia.BeginDraw().Clear(new SkiaSharp.SKColor(0x30, 0x60, 0xc0, 0xff));
            skia.EndDraw();

            Assert.True(surface.TryAcquire(out var frame));
            using (frame)
            {
                var buffer = Assert.IsType<MappedBuffer>(frame.Buffer);
                Assert.Equal(Drawn, PixelAt(buffer, 32, 32));
            }
        }
    }

    private static uint Sample(Basin.Render.Vulkan.VulkanRenderer renderer, IBuffer source)
    {
        var target = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
        var texture = renderer.ImportTexture(source);
        Assert.NotNull(texture);
        using (texture)
        {
            var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(0, 0, 64, 64) });
            Assert.True(pass.Submit());
        }

        var pixel = PixelAt(target, 32, 32);
        target.Destroy();
        return pixel;
    }

    private static uint PixelAt(IBuffer buffer, int x, int y)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                return *((uint*)(view.Data + (nint)y * view.Stride) + x) | 0xff000000u;
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    private sealed class FirstOnlyAllocator(IAllocator inner) : IAllocator
    {
        private int _calls;

        public DrmFormatSet Formats => inner.Formats;

        public BufferBase? Allocate(int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers, BufferUse use)
            => _calls++ == 0 ? inner.Allocate(width, height, format, modifiers, use) : null;

        public void Dispose()
        {
        }
    }

    private sealed class MappedAllocator : IAllocator
    {
        public DrmFormatSet Formats { get; } = new();

        public BufferBase? Allocate(int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers, BufferUse use)
            => new MappedBuffer(width, height, format);

        public void Dispose()
        {
        }
    }

    private sealed unsafe class MappedBuffer : BufferBase
    {
        private readonly int _stride;
        private void* _storage;

        public MappedBuffer(int width, int height, DrmFormat format)
            : base(width, height)
        {
            Format = format;
            _stride = width * 4;
            _storage = System.Runtime.InteropServices.NativeMemory.AllocZeroed((nuint)(_stride * height));
        }

        public override DrmFormat Format { get; }

        protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
        {
            view = new BufferDataView((nint)_storage, _stride, Format);
            return true;
        }

        protected override void OnFreeStorage()
        {
            System.Runtime.InteropServices.NativeMemory.Free(_storage);
            _storage = null;
        }
    }
}
