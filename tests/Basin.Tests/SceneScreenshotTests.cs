using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public class SceneScreenshotTests
{
    [Fact]
    public void Write_renders_the_output_to_a_png()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host);
        var path = Path.Combine(Path.GetTempPath(), $"basin-shot-{Environment.ProcessId}.png");
        try
        {
            Assert.True(SceneScreenshot.Write(host.Scene, host.Renderer, host.Output, path));
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Write_blits_the_cursor_when_one_is_given()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host);
        var sprite = new MemoryBuffer(8, 8, DrmFormat.Argb8888);
        var path = Path.Combine(Path.GetTempPath(), $"basin-shot-cursor-{Environment.ProcessId}.png");
        try
        {
            Assert.True(SceneScreenshot.Write(
                host.Scene, host.Renderer, host.Output, path,
                new CursorBlit(sprite, new Box(4, 4, 8, 8))));
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
            sprite.Destroy();
        }
    }

    [Fact]
    public void WritePresented_reports_the_missing_frame()
    {
        using var host = new CompositorTestHost(64, 64);
        var path = Path.Combine(Path.GetTempPath(), $"basin-shotraw-{Environment.ProcessId}.png");
        Assert.Equal(ScreenshotOutcome.NoFrame, SceneScreenshot.WritePresented(null, host.Renderer, path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void WritePresented_writes_a_readable_buffer()
    {
        using var host = new CompositorTestHost(64, 64);
        var presented = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        var path = Path.Combine(Path.GetTempPath(), $"basin-shotraw-ok-{Environment.ProcessId}.png");
        try
        {
            Assert.Equal(
                ScreenshotOutcome.Written,
                SceneScreenshot.WritePresented(presented, host.Renderer, path));
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
            presented.Destroy();
        }
    }

    private static void MapSurface(CompositorTestHost host)
    {
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(32, 32, Fill.Solid(32, 32, 0xffff0000));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 32, 32);
        surface.Commit();
        host.PumpToServer();
    }
}
