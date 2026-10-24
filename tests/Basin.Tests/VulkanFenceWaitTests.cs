using System.Runtime.InteropServices;
using Basin.Render.Vulkan;
using Xunit;

namespace Basin.Tests;

public sealed class VulkanFenceWaitTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    [Fact]
    public void A_failed_import_closes_its_duplicate_and_waits_on_the_CPU()
    {
        CompositorTestHost.SkipUnlessRunnable("vulkan");
        using var host = new CompositorTestHost(renderer: "vulkan");
        var device = ((VulkanRenderer)host.Renderer).Device;
        Assert.SkipUnless(device.FenceWait.IsGpuSide, "no external semaphore import on this device");

        var notASyncFile = memfd_create("basin-not-a-sync-file", 1);
        Assert.True(notASyncFile >= 0);
        try
        {
            device.FenceWait.Wait(notASyncFile);
            var slots = device.FenceWait.Slots;
            for (var i = 0; i < 9; i++)
            {
                device.FenceWait.Wait(notASyncFile);
            }

            Assert.Equal(slots, device.FenceWait.Slots);
        }
        finally
        {
            _ = close(notASyncFile);
        }
    }

    [Fact]
    public void Back_to_back_waits_never_reuse_a_semaphore_still_in_flight()
    {
        CompositorTestHost.SkipUnlessRunnable("vulkan");
        using var host = new CompositorTestHost(renderer: "vulkan");
        var device = ((VulkanRenderer)host.Renderer).Device;
        Assert.SkipUnless(device.FenceWait.IsGpuSide, "no external semaphore import on this device");

        for (var i = 0; i < 6; i++)
        {
            var fence = device.ExportQueueFence();
            Assert.True(fence >= 0);
            device.FenceWait.Wait(fence);
            RenderFences.CloseFence(fence);
        }

        var grown = device.FenceWait.Slots;
        Assert.InRange(grown, 1, 6);
        device.WaitFor(device.Submit(device.BeginCommands()));
        _ = device.Api.QueueWaitIdle(device.Queue);

        var fenceAgain = device.ExportQueueFence();
        device.FenceWait.Wait(fenceAgain);
        RenderFences.CloseFence(fenceAgain);
        Assert.Equal(grown, device.FenceWait.Slots);
    }
}
