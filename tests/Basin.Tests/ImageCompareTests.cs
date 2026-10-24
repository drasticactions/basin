using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class ImageCompareTests
{
    private static byte[] Image(int width, int height, Func<int, int, (byte R, byte G, byte B)> color)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = color(x, y);
                var i = ((y * width) + x) * 4;
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (r, g, b, 255);
            }
        }

        return rgba;
    }

    [Fact]
    public void Identical_shifted_tinted_and_resized_references()
    {
        var actual = Image(20, 10, (x, y) => (x < 10 ? (byte)200 : (byte)20, 40, 60));
        var same = ImageCompare.Compare(actual, 20, 10, actual, 20, 10, tolerance: 2, out var diff);
        Assert.Equal((true, 0, 0, default(Box)), (same.SameSize, same.Changed, same.MaxDelta, same.ChangedBox));
        Assert.NotNull(diff);

        var shifted = Image(20, 10, (x, y) => (x < 11 ? (byte)200 : (byte)20, 40, 60));
        var moved = ImageCompare.Compare(actual, 20, 10, shifted, 20, 10, 2, out diff);
        Assert.Equal((10, 180, new Box(10, 0, 1, 10)), (moved.Changed, moved.MaxDelta, moved.ChangedBox));
        Assert.Equal(255, diff![((0 * 20) + 10) * 4]);

        var under = ImageCompare.Compare(actual, 20, 10, Image(20, 10, (x, y) => (x < 10 ? (byte)202 : (byte)22, 40, 60)), 20, 10, 2, out _);
        Assert.Equal((0, 2), (under.Changed, under.MaxDelta));
        var over = ImageCompare.Compare(actual, 20, 10, Image(20, 10, (x, y) => (x < 10 ? (byte)203 : (byte)23, 40, 60)), 20, 10, 2, out _);
        Assert.Equal((200, 3), (over.Changed, over.MaxDelta));

        var resized = ImageCompare.Compare(actual, 20, 10, Image(10, 10, (_, _) => (0, 0, 0)), 10, 10, 2, out diff);
        Assert.False(resized.SameSize);
        Assert.Null(diff);
    }
}
