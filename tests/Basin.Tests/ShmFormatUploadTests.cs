using Xunit;

namespace Basin.Tests;

public sealed class ShmFormatUploadTests
{
    private const int R = 0x80;

    private const int G = 0x40;

    private const int B = 0xC0;

    private static int To(int value, int bits) => (int)Math.Round(value / 255.0 * ((1 << bits) - 1));

    private static ushort Half(int value) => BitConverter.HalfToUInt16Bits((Half)(value / 255f));

    private static unsafe void Fill(MemoryBuffer content, DrmFormat format)
    {
        Assert.True(content.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < content.Height; y++)
        {
            var row = view.Data + (y * view.Stride);
            for (var x = 0; x < content.Width; x++)
            {
                switch (format)
                {
                    case DrmFormat.Argb8888 or DrmFormat.Xrgb8888:
                        ((uint*)row)[x] = 0xFF000000u | (R << 16) | (G << 8) | B;
                        break;
                    case DrmFormat.Abgr8888 or DrmFormat.Xbgr8888:
                        ((uint*)row)[x] = 0xFF000000u | (B << 16) | (G << 8) | R;
                        break;
                    case DrmFormat.Argb2101010 or DrmFormat.Xrgb2101010:
                        ((uint*)row)[x] = (3u << 30) | ((uint)To(R, 10) << 20) | ((uint)To(G, 10) << 10) | (uint)To(B, 10);
                        break;
                    case DrmFormat.Abgr2101010 or DrmFormat.Xbgr2101010:
                        ((uint*)row)[x] = (3u << 30) | ((uint)To(B, 10) << 20) | ((uint)To(G, 10) << 10) | (uint)To(R, 10);
                        break;
                    case DrmFormat.Rgb565:
                        ((ushort*)row)[x] = (ushort)((To(R, 5) << 11) | (To(G, 6) << 5) | To(B, 5));
                        break;
                    case DrmFormat.Abgr16161616f or DrmFormat.Xbgr16161616f:
                        var pixel = (ushort*)row + (x * 4);
                        pixel[0] = Half(R);
                        pixel[1] = Half(G);
                        pixel[2] = Half(B);
                        pixel[3] = BitConverter.HalfToUInt16Bits((Half)1f);
                        break;
                }
            }
        }

        content.EndDataAccess();
    }

    [Fact]
    public void Pixman_declines_a_format_it_cannot_read_rather_than_throwing()
    {
        using var host = new CompositorTestHost(renderer: "pixman");
        var content = new MemoryBuffer(4, 4, DrmFormat.Abgr16161616f);
        Assert.DoesNotContain(DrmFormat.Abgr16161616f, host.Renderer.ShmTextureFormats);
        Assert.Null(host.Renderer.ImportTexture(content));
        content.Destroy();
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    [InlineData("skia-gl")]
    [InlineData("skia-vulkan")]
    public void Every_shm_format_a_renderer_declares_draws_its_color(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        Assert.Contains(DrmFormat.Argb8888, host.Renderer.ShmTextureFormats);
        foreach (var format in host.Renderer.ShmTextureFormats)
        {
            const int width = 7;
            var content = new MemoryBuffer(width, 4, format);
            Fill(content, format);
            var texture = host.Renderer.ImportTexture(content);
            Assert.True(texture is not null, $"{renderer} declined {format}");
            var target = new MemoryBuffer(16, 8, DrmFormat.Argb8888);
            var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddTexture(texture!, new TextureRenderOptions { DstBox = new Box(0, 0, width, 4) });
            Assert.True(pass.Submit());
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var read));
            uint pixel;
            unsafe
            {
                pixel = *(uint*)(read.Data + (2 * read.Stride) + (3 * 4));
            }

            target.EndDataAccess();
            var slack = format == DrmFormat.Rgb565 ? 5 : 2;
            var got = ((int)((pixel >> 16) & 0xFF), (int)((pixel >> 8) & 0xFF), (int)(pixel & 0xFF));
            Assert.True(
                Math.Abs(got.Item1 - R) <= slack && Math.Abs(got.Item2 - G) <= slack && Math.Abs(got.Item3 - B) <= slack,
                $"{renderer} {format}: got {got}");
            texture!.Dispose();
            target.Destroy();
            content.Destroy();
        }
    }
}
