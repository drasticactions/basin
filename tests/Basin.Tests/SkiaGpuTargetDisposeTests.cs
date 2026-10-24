using Basin.Render.Skia;
using Basin.UI.Skia;
using Xunit;

namespace Basin.Tests;

public sealed class SkiaGpuTargetDisposeTests
{
    [Fact]
    public void A_Ganesh_Vulkan_target_disposed_twice_frees_once()
    {
        CompositorTestHost.SkipUnlessRunnable("skia-vulkan");
        LeakTracking.Require();
        using var host = new CompositorTestHost(renderer: "skia-vulkan");
        var renderer = (SkiaVulkanRenderer)host.Renderer;
        var buffer = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        var target = SkiaVulkanTarget.Create(renderer.Device, renderer.Context, buffer);
        target.Dispose();
        target.Dispose();
        buffer.Destroy();
    }

    [Fact]
    public void A_Graphite_target_disposed_twice_frees_once()
    {
        CompositorTestHost.SkipUnlessRunnable("skia-graphite");
        LeakTracking.Require();
        using var host = new CompositorTestHost(renderer: "skia-graphite");
        var renderer = (SkiaGraphiteRenderer)host.Renderer;
        var buffer = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        var target = SkiaGraphiteTarget.Create(renderer.Device, renderer.Recorder, buffer);
        target.Dispose();
        target.Dispose();
        buffer.Destroy();
    }

    [Fact]
    public void A_wrap_factory_that_throws_leaves_no_disposed_wrap_behind()
    {
        CompositorTestHost.SkipUnlessRunnable("skia-vulkan");
        LeakTracking.Require();
        using var host = new CompositorTestHost(renderer: "skia-vulkan");
        var renderer = (SkiaVulkanRenderer)host.Renderer;
        var calls = 0;
        var surface = new SkiaVulkanUISurface(
            renderer.Device,
            buffer => ++calls == 1
                ? new GaneshDrawTarget(SkiaVulkanTarget.Create(renderer.Device, renderer.Context, buffer))
                : throw new NotSupportedException("the second wrap fails"),
            () => renderer.Context.Flush(submit: true, synchronous: false),
            null,
            []);
        Assert.True(surface.Configure(32, 32, 1));
        surface.BeginDraw();
        surface.EndDraw();
        Assert.True(surface.Configure(48, 48, 1));
        Assert.Throws<NotSupportedException>(() => surface.BeginDraw());
        surface.Dispose();
        Assert.Equal(2, calls);
    }
}
