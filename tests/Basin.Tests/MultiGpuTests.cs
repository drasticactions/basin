using Basin.Render.Gl;
using Basin.Render.Vulkan;
using Xunit;

namespace Basin.Tests;

public sealed class MultiGpuTests
{
    [Fact]
    public void Device_registry_reports_this_machines_cards()
    {
        var devices = DrmDevices.Enumerate();
        Assert.SkipWhen(devices.Count == 0, "no DRM devices");

        var primary = DrmDevices.PickPrimary(devices);
        if (devices.Any(d => d.HasConnectors))
        {
            Assert.NotNull(primary);
            Assert.True(primary!.HasConnectors);
        }
        else
        {
            Assert.Null(primary);
        }
    }

    [Fact]
    public void Cross_device_blit_round_trips_pixels()
    {
        var devices = DrmDevices.Enumerate().Where(d => d.RenderNodePath is not null).ToList();
        var primaryNode = CompositorTestHost.RenderNodePath;
        var otherNode = devices.Select(d => d.RenderNodePath!).FirstOrDefault(n => n != primaryNode);
        Assert.SkipWhen(otherNode is null || !File.Exists(primaryNode), "needs two GPUs with render nodes");
        Assert.SkipWhen(VulkanRunnability.BlockerFor(otherNode!) is not null, VulkanRunnability.BlockerFor(otherNode!) ?? "");

        using var gl = new GlRenderer(primaryNode);
        using var allocator = gl.Device.CreateAllocator();
        var source = allocator.Allocate(64, 64, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Render);
        Assert.NotNull(source);
        var fill = gl.BeginBufferPass(source!, new RenderPassOptions());
        fill.AddRect(new RenderColor(1, 0, 0, 1), new Box(0, 0, 64, 64));
        fill.AddRect(new RenderColor(0, 1, 0, 1), new Box(32, 0, 32, 64));
        Assert.True(fill.Submit());

        using var blitter = new VulkanDeviceBlitter(otherNode!);
        var conversion = blitter.Convert(source!);
        Assert.SkipWhen(conversion is null, "other GPU cannot import linear xrgb (unexpected but device-specific)");

        var texture = gl.ImportTexture(conversion!.Buffer);
        Assert.NotNull(texture);
        var target = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
        var pass = gl.BeginBufferPass(target, new RenderPassOptions());
        pass.AddTexture(texture!, new TextureRenderOptions { DstBox = new Box(0, 0, 64, 64) });
        Assert.True(pass.Submit());

        Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                var left = *(uint*)(view.Data + 32 * view.Stride + 8 * 4);
                var right = *(uint*)(view.Data + 32 * view.Stride + 56 * 4);
                Assert.Equal(0xFF0000u, left & 0xFFFFFF);
                Assert.Equal(0x00FF00u, right & 0xFFFFFF);
            }
        }
        finally
        {
            target.EndDataAccess();
        }

        texture!.Dispose();
        conversion.Dispose();
        target.Destroy();
        (source as BufferBase)!.Destroy();
    }
}
