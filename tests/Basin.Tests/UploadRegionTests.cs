using Basin.Render.Vulkan;
using Xunit;

namespace Basin.Tests;

public sealed class UploadRegionTests
{
    [Fact]
    public void Vulkan_uploads_two_far_apart_rects_not_their_hull()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var device = new VulkanDevice(CompositorTestHost.RenderNodePath);
        using var staging = new VulkanStagingPool(device);
        var buffer = new MemoryBuffer(256, 256, DrmFormat.Argb8888);
        try
        {
            using var upload = new VulkanUploadImage(device, buffer);

            Assert.True(upload.PrepareUpload(staging));
            var afterFull = upload.UploadedBytes;
            Assert.Equal(256UL * 256 * 4, afterFull);

            upload.MarkDirty(new Box(0, 0, 16, 16));
            upload.MarkDirty(new Box(240, 240, 16, 16));
            Assert.True(upload.PrepareUpload(staging));
            Assert.Equal(afterFull + (2UL * 16 * 16 * 4), upload.UploadedBytes);
        }
        finally
        {
            buffer.Destroy();
        }
    }

    [Fact]
    public void Gl_uploads_two_far_apart_rects_not_their_hull()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        using var device = new Basin.Render.Gl.GlDevice(CompositorTestHost.RenderNodePath);
        var buffer = new MemoryBuffer(256, 256, DrmFormat.Argb8888);
        try
        {
            using var texture = new Basin.Render.Gl.GlShmTexture(device, buffer);
            Assert.True(texture.Acquire(out _));
            var afterFull = texture.UploadedBytes;
            Assert.Equal(256UL * 256 * 4, afterFull);

            texture.MarkDirty(new Box(0, 0, 16, 16));
            texture.MarkDirty(new Box(240, 240, 16, 16));
            Assert.True(texture.Acquire(out _));
            Assert.Equal(afterFull + (2UL * 16 * 16 * 4), texture.UploadedBytes);
        }
        finally
        {
            buffer.Destroy();
        }
    }

    [Fact]
    public void Vulkan_unions_into_the_hull_past_the_rect_budget()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var device = new VulkanDevice(CompositorTestHost.RenderNodePath);
        using var staging = new VulkanStagingPool(device);
        var buffer = new MemoryBuffer(64, 64, DrmFormat.Argb8888);
        try
        {
            using var upload = new VulkanUploadImage(device, buffer);
            Assert.True(upload.PrepareUpload(staging));
            var afterFull = upload.UploadedBytes;

            for (var i = 0; i < DamageRects.Capacity + 1; i++)
            {
                upload.MarkDirty(new Box(i * 8, i * 8, 4, 4));
            }

            Assert.True(upload.PrepareUpload(staging));
            var span = (DamageRects.Capacity * 8) + 4;
            Assert.Equal(afterFull + ((ulong)(span * span) * 4), upload.UploadedBytes);
        }
        finally
        {
            buffer.Destroy();
        }
    }
}
