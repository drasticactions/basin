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

    private static uint PixelAt(MemoryBuffer buffer, int x, int y)
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
}
