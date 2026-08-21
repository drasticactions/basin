using Basin.Render.Vulkan;
using Xunit;

namespace Basin.Tests;

public sealed class VulkanProbeTests
{
    [Fact]
    public void Rect_and_texture_render()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new VulkanRenderer(CompositorTestHost.RenderNodePath);
        var target = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
        var source = new MemoryBuffer(16, 16, DrmFormat.Argb8888);
        unsafe
        {
            Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
            for (var y = 0; y < 16; y++)
            {
                var row = (uint*)(view.Data + y * view.Stride);
                for (var x = 0; x < 16; x++)
                {
                    row[x] = 0xFF00FF00;
                }
            }

            source.EndDataAccess();
        }

        var texture = renderer.ImportTexture(source);
        Assert.NotNull(texture);
        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(1, 0, 0, 1), new Box(0, 0, 64, 64));
        pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(8, 8, 16, 16) });
        Assert.True(pass.Submit());

        unsafe
        {
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
            var corner = *(uint*)view.Data;
            var inside = *(uint*)(view.Data + 12 * view.Stride + 12 * 4);
            target.EndDataAccess();
            Assert.Equal(0xFFFF0000u, corner);
            Assert.Equal(0xFF00FF00u, inside);
        }

        texture.Dispose();
        target.Destroy();
        source.Destroy();
    }
}
