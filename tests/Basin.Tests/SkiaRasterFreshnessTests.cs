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
}
