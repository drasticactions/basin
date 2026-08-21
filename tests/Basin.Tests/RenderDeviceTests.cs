using Xunit;

namespace Basin.Tests;

public sealed class RenderDeviceTests
{
    [Theory]
    [InlineData("pixman", false)]
    [InlineData("skia", false)]
    [InlineData("gl", true)]
    [InlineData("skia-gl", true)]
    [InlineData("vulkan", true)]
    [InlineData("skia-vulkan", true)]
    [InlineData("skia-graphite", true)]
    [InlineData("impeller", true)]
    public void Renderer_reports_the_device_it_draws_on(string renderer, bool onGpu)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);

        using var host = new CompositorTestHost(renderer: renderer);
        var device = host.Renderer.Device;
        if (!onGpu)
        {
            Assert.Null(device);
            return;
        }

        Assert.NotNull(device);
        Assert.True(device!.DrmFd >= 0);
        Assert.True(File.Exists(device.DevicePath), $"device path '{device.DevicePath}' does not exist");
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("skia-gl")]
    [InlineData("vulkan")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    [InlineData("impeller")]
    public void Syncobj_manager_builds_from_the_declared_device(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);

        using var host = new CompositorTestHost(renderer: renderer);
        var device = host.Renderer.Device;
        Assert.NotNull(device);

        using var manager = new Basin.Desktop.LinuxDrmSyncobjManager(host.Display, host.Compositor, new Basin.Backend.Drm.DrmSyncDevice(device!.DrmFd));
        manager.DeclareRenderer(host.Renderer);

        var bound = false;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) => bound |= e.Interface == "wp_linux_drm_syncobj_manager_v1";
        host.PumpToClient();

        Assert.True(bound, "the syncobj global never reached a client");
    }
}
