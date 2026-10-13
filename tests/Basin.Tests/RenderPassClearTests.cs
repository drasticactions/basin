using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class RenderPassClearTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" };

    private static uint At(IBuffer buffer, int x, int y)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var access));
        try
        {
            unsafe
            {
                return ((uint*)(access.Data + (y * access.Stride)))[x];
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Clear_replaces_the_whole_target_without_blending(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var target = new MemoryBuffer(32, 24, DrmFormat.Argb8888);
        try
        {
            var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddRect(new RenderColor(1f, 0f, 0f, 1f), new Box(0, 0, 32, 24));
            pass.Clear(new RenderColor(0f, 0f, 0f, 0f));
            pass.AddRect(new RenderColor(0f, 0f, 1f, 1f), new Box(4, 4, 8, 8));
            Assert.True(pass.Submit());

            Assert.Equal(0x00000000u, At(target, 20, 20));
            Assert.Equal(0xFF0000FFu, At(target, 6, 6));
        }
        finally
        {
            target.Destroy();
        }
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void A_shape_mask_keeps_the_shape_and_clears_the_rest_every_time(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var source = new MemoryBuffer(20, 20, DrmFormat.Xrgb8888);
        Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var access));
        unsafe
        {
            for (var y = 0; y < 20; y++)
            {
                new Span<uint>((void*)(access.Data + (y * access.Stride)), 20).Fill(0x00FFFFFFu);
            }
        }

        source.EndDataAccess();
        using var mask = new XWaylandShapeMask(host.Renderer, host.DeviceAllocator);
        using var left = new Pixman.PixmanRegion32(0, 0, 10, 20);
        using var right = new Pixman.PixmanRegion32(10, 0, 10, 20);
        try
        {
            for (var round = 0; round < 4; round++)
            {
                var shape = round % 2 == 0 ? left : right;
                var masked = mask.Render(source, shape);
                Assert.NotNull(masked);
                var copy = new MemoryBuffer(20, 20, DrmFormat.Argb8888);
                try
                {
                    var readback = host.Renderer.BeginBufferPass(copy, new RenderPassOptions());
                    readback.Clear(new RenderColor(0f, 0f, 0f, 0f));
                    var texture = host.Renderer.ImportTexture(masked!);
                    Assert.NotNull(texture);
                    readback.AddTexture(texture!, new TextureRenderOptions { DstBox = new Box(0, 0, 20, 20) });
                    Assert.True(readback.Submit());
                    texture!.Dispose();

                    var inside = round % 2 == 0 ? 2 : 17;
                    var outside = round % 2 == 0 ? 17 : 2;
                    Assert.Equal(0xFFFFFFFFu, At(copy, inside, 10));
                    Assert.Equal(0u, At(copy, outside, 10) >> 24);
                }
                finally
                {
                    copy.Destroy();
                }
            }
        }
        finally
        {
            source.Destroy();
        }
    }
}
