using Xunit;

namespace Basin.Tests;

public sealed class RenderTargetSeedTests
{
    private static MemoryBuffer SolidBuffer(int width, int height, uint pixel)
    {
        var buffer = new MemoryBuffer(width, height, DrmFormat.Argb8888);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)(view.Data + y * view.Stride);
                for (var x = 0; x < width; x++)
                {
                    row[x] = pixel;
                }
            }
        }

        buffer.EndDataAccess();
        return buffer;
    }

    private static unsafe uint PixelAt(IBuffer buffer, int x, int y)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            return *(uint*)(view.Data + y * view.Stride + x * 4);
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("skia")]
    [InlineData("gl")]
    [InlineData("skia-gl")]
    public void A_partial_pass_composites_over_the_target_buffers_own_pixels(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var target = SolidBuffer(32, 32, 0xFFFF8000);
        var content = SolidBuffer(8, 8, 0xFF204060);
        try
        {
            var texture = host.Renderer.ImportTexture(content);
            Assert.NotNull(texture);

            var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(0, 0, 8, 8) });
            Assert.True(pass.Submit());

            var covered = PixelAt(target, 4, 4);
            var untouched = PixelAt(target, 20, 20);
            Assert.True((covered & 0xFF000000) == 0xFF000000 && Near(covered, 0xFF204060),
                $"the covered area holds {covered:x8}");
            Assert.True(Near(untouched, 0xFFFF8000),
                $"the untouched area holds {untouched:x8} instead of the buffer's own pixels");

            texture!.Dispose();
        }
        finally
        {
            target.Destroy();
            content.Destroy();
        }
    }

    private static bool Near(uint actual, uint expected)
    {
        for (var shift = 0; shift < 32; shift += 8)
        {
            if (Math.Abs((int)((actual >> shift) & 0xff) - (int)((expected >> shift) & 0xff)) > 3)
            {
                return false;
            }
        }

        return true;
    }
}
