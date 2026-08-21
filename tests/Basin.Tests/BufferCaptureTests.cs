using Basin.Diagnostics;
using Basin.Render.Gl;
using Xunit;

namespace Basin.Tests;

public sealed class BufferCaptureTests
{
    [Fact]
    public void Png_round_trips_what_it_wrote()
    {
        var rgba = new byte[4 * 3 * 4];
        for (var i = 0; i < rgba.Length; i++)
        {
            rgba[i] = (byte)(i * 7);
        }

        var (decoded, width, height) = PngCodec.Decode(PngCodec.Encode(rgba, 4, 3));
        Assert.Equal(4, width);
        Assert.Equal(3, height);
        Assert.Equal(rgba, decoded);
    }

    [Theory]
    [InlineData(DrmFormat.Xrgb8888, 0x00FF8040u, 0xFFu)]
    [InlineData(DrmFormat.Argb8888, 0x40FF8040u, 0x40u)]
    public void Alpha_follows_the_buffer_format(DrmFormat format, uint pixel, uint expectedAlpha)
    {
        var buffer = new MemoryBuffer(2, 2, format);
        try
        {
            Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
            unsafe
            {
                for (var y = 0; y < 2; y++)
                {
                    var row = (uint*)(view.Data + (y * view.Stride));
                    row[0] = pixel;
                    row[1] = pixel;
                }
            }

            buffer.EndDataAccess();

            var rgba = BufferCapture.ReadRgba(buffer);
            Assert.Equal(2 * 2 * 4, rgba.Length);
            Assert.Equal(0xFF, rgba[0]);
            Assert.Equal(0x80, rgba[1]);
            Assert.Equal(0x40, rgba[2]);
            Assert.Equal(expectedAlpha, rgba[3]);
        }
        finally
        {
            buffer.Destroy();
        }
    }

    [Fact]
    public void Unmappable_buffer_reads_back_through_the_renderer()
    {
        Assert.SkipWhen(!File.Exists(CompositorTestHost.RenderNodePath), "no render node");

        using var gl = new GlRenderer(CompositorTestHost.RenderNodePath);
        using var allocator = gl.Device.CreateAllocator();
        var modifiers = allocator.Formats.ModifiersOf(DrmFormat.Xrgb8888).ToArray();
        var source = allocator.Allocate(64, 64, DrmFormat.Xrgb8888, modifiers, BufferUse.Render)
            ?? allocator.Allocate(64, 64, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Render);
        Assert.SkipWhen(source is null, "GBM declined an Xrgb8888 render target");

        var fill = gl.BeginBufferPass(source!, new RenderPassOptions());
        fill.AddRect(new RenderColor(1, 0, 0, 1), new Box(0, 0, 64, 64));
        fill.AddRect(new RenderColor(0, 1, 0, 1), new Box(32, 0, 32, 64));
        Assert.True(fill.Submit());

        if (source!.BeginDataAccess(BufferDataAccess.Read, out _))
        {
            source.EndDataAccess();
            Assert.Skip("gbm buffers are CPU-mappable on this driver; the renderer path is not what would run");
        }

        Assert.False(BufferCapture.TryReadRgba(source, out _));

        Assert.True(BufferCapture.TryReadRgba(source, gl, out var rgba));
        Assert.Equal(64 * 64 * 4, rgba!.Length);
        var left = ((32 * 64) + 8) * 4;
        var right = ((32 * 64) + 56) * 4;
        Assert.Equal(0xFF, rgba[left]);
        Assert.Equal(0x00, rgba[left + 1]);
        Assert.Equal(0x00, rgba[right]);
        Assert.Equal(0xFF, rgba[right + 1]);
        Assert.Equal(0xFF, rgba[left + 3]);
    }
}
