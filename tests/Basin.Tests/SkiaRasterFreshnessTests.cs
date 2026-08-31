using Basin.Render.Skia;
using Xunit;

namespace Basin.Tests;

public sealed class SkiaRasterFreshnessTests
{
    [Fact]
    public void Mutated_pixels_reach_the_target_without_a_dirty_rebuild()
    {
        CompositorTestHost.SkipUnlessRunnable("skia");
        using var host = new CompositorTestHost(renderer: "skia");
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Solid(64, 48, 0xFFFF0000));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpToServer();
        host.Output.StepFrame();
        host.RenderFrame();
        Assert.Equal(0xFFFF0000u, host.Pixel(5, 5));

        unsafe
        {
            var pixels = (uint*)buffer.Data;
            for (var i = 0; i < 64 * 48; i++)
            {
                pixels[i] = 0xFF0000FF;
            }
        }

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpToServer();
        host.Output.StepFrame();
        host.RenderFrame();
        Assert.Equal(0xFF0000FFu, host.Pixel(5, 5));
    }

    [Fact]
    public void A_dirty_mark_yields_a_fresh_image_where_a_clean_reuse_keeps_it()
    {
        var buffer = new MemoryBuffer(8, 8, DrmFormat.Argb8888);
        using var renderer = new SkiaRenderer();
        var texture = Assert.IsAssignableFrom<ISkiaTexture>(renderer.ImportTexture(buffer));
        Assert.True(texture.Acquire(out var first));
        var cleanId = first.UniqueId;
        texture.Release();

        Assert.True(texture.Acquire(out var reused));
        Assert.Equal(cleanId, reused.UniqueId);
        texture.Release();

        Assert.IsAssignableFrom<IRefreshableTexture>(texture).MarkDirty();
        Assert.True(texture.Acquire(out var fresh));
        Assert.NotEqual(cleanId, fresh.UniqueId);
        texture.Release();

        texture.Dispose();
        buffer.Destroy();
    }
}
