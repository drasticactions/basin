using System.Runtime.Versioning;
using Basin.Capabilities;
using Basin.Video.VideoToolbox;
using Xunit;

namespace Basin.Tests;

[SupportedOSPlatform("macos")]
public sealed unsafe class PixelRowsTests
{
    private static void SkipOffApple() => Assert.SkipWhen(!OperatingSystem.IsMacOS(), "the VideoToolbox copy is built for Apple hosts");

    private static byte[] Bgra(int width, int height, int seed)
    {
        var random = new Random(seed);
        var pixels = new byte[width * height * 4];
        random.NextBytes(pixels);
        return pixels;
    }

    private static uint Widen(uint value) => (value << 2) | (value >> 6);

    [Theory]
    [InlineData(7, 3)]
    [InlineData(300, 2)]
    [InlineData(1, 1)]
    public void The_ten_bit_rows_widen_every_channel_and_swap_red_and_blue_for_the_bgr_order(int width, int height)
    {
        SkipOffApple();
        var source = Bgra(width, height, width);
        var rgb = new uint[width * height];
        var bgr = new uint[width * height];
        fixed (byte* from = source)
        fixed (uint* toRgb = rgb)
        fixed (uint* toBgr = bgr)
        {
            PixelRows.Copy(from, width * 4, (nint)toRgb, width * 4, width, height, DrmFormat.Xrgb2101010);
            PixelRows.Copy(from, width * 4, (nint)toBgr, width * 4, width, height, DrmFormat.Xbgr2101010);
        }

        for (var i = 0; i < width * height; i++)
        {
            uint b = source[i * 4], g = source[(i * 4) + 1], r = source[(i * 4) + 2];
            Assert.Equal((3u << 30) | (Widen(r) << 20) | (Widen(g) << 10) | Widen(b), rgb[i]);
            Assert.Equal((3u << 30) | (Widen(b) << 20) | (Widen(g) << 10) | Widen(r), bgr[i]);
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(301)]
    public void The_abgr_row_swaps_red_and_blue_and_keeps_alpha(int width)
    {
        SkipOffApple();
        var source = Bgra(width, 1, width);
        var swapped = new byte[width * 4];
        fixed (byte* from = source)
        fixed (byte* to = swapped)
        {
            PixelRows.Copy(from, width * 4, (nint)to, width * 4, width, 1, DrmFormat.Abgr8888);
        }

        for (var x = 0; x < width; x++)
        {
            Assert.Equal(source[(x * 4) + 2], swapped[x * 4]);
            Assert.Equal(source[(x * 4) + 1], swapped[(x * 4) + 1]);
            Assert.Equal(source[x * 4], swapped[(x * 4) + 2]);
            Assert.Equal(source[(x * 4) + 3], swapped[(x * 4) + 3]);
        }
    }
}
