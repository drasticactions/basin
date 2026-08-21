using Basin.Transport.Waypipe;
using Xunit;

namespace Basin.Tests;

public sealed class ChannelFormatRenderTests
{
    private static readonly (DrmFormat Format, uint OpaqueRed)[] Rows =
    [
        (DrmFormat.Argb8888, 0xFFFF0000),
        (DrmFormat.Xrgb8888, 0xFFFF0000),
        (DrmFormat.Abgr8888, 0xFF0000FF),
        (DrmFormat.Xbgr8888, 0xFF0000FF),
        (DrmFormat.Argb2101010, 0xFFF00000),
        (DrmFormat.Xrgb2101010, 0xFFF00000),
        (DrmFormat.Abgr2101010, 0xC00003FF),
        (DrmFormat.Xbgr2101010, 0xC00003FF),
    ];

    [Fact]
    public void Every_advertised_channel_format_has_a_row()
    {
        foreach (var format in WaypipeGlobals.ChannelFormats.Formats)
        {
            Assert.True(
                Array.Exists(Rows, row => row.Format == format),
                $"the channel advertises fourcc 0x{(uint)format:x8} and no row here draws it");
        }
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("skia")]
    public void A_raster_renderer_draws_every_advertised_channel_format(string renderer)
    {
        using var renderStack = RasterStack(renderer);

        foreach (var format in WaypipeGlobals.ChannelFormats.Formats)
        {
            var row = Array.Find(Rows, candidate => candidate.Format == format);
            Assert.Equal(format, row.Format);

            var content = Filled(2, 2, format, row.OpaqueRed);
            var target = Filled(8, 8, DrmFormat.Argb8888, 0xFF000000);
            try
            {
                var texture = renderStack.ImportTexture(content);
                Assert.NotNull(texture);

                var pass = renderStack.BeginBufferPass(target, new RenderPassOptions());
                pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(0, 0, 8, 8) });
                Assert.True(pass.Submit());

                var drawn = PixelAt(target, 4, 4);
                Assert.True(
                    Near(drawn, 0xFFFF0000),
                    $"fourcc 0x{(uint)format:x8} drew {drawn:x8} where opaque red was expected");

                texture!.Dispose();
            }
            finally
            {
                content.Destroy();
                target.Destroy();
            }
        }
    }

    private static IRenderer RasterStack(string renderer)
    {
        try
        {
            return renderer == "pixman"
                ? new Basin.Render.Pixman.PixmanRenderer()
                : new Basin.Render.Skia.SkiaRenderer();
        }
        catch (DllNotFoundException missing)
        {
            Assert.Skip($"{renderer} does not load here: {missing.Message}");
            throw;
        }
    }

    private static unsafe MemoryBuffer Filled(int width, int height, DrmFormat format, uint pixel)
    {
        var buffer = new MemoryBuffer(width, height, format);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < height; y++)
        {
            var row = (uint*)(view.Data + (y * view.Stride));
            for (var x = 0; x < width; x++)
            {
                row[x] = pixel;
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
            return *(uint*)(view.Data + (y * view.Stride) + (x * 4));
        }
        finally
        {
            buffer.EndDataAccess();
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
