namespace Basin.Diagnostics;

public static class ImageCompare
{
    public static ImageComparison Compare(
        ReadOnlySpan<byte> actual, int width, int height, ReadOnlySpan<byte> expected, int expectedWidth, int expectedHeight, int channel,
        out byte[]? diff)
    {
        diff = null;
        if (width != expectedWidth || height != expectedHeight)
        {
            return new ImageComparison(SameSize: false, Changed: width * height, MaxDelta: 255, ChangedBox: new Box(0, 0, width, height));
        }

        var image = new byte[width * height * 4];
        var changed = 0;
        var maxDelta = 0;
        int left = width, top = height, right = 0, bottom = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                var delta = 0;
                for (var c = 0; c < 4; c++)
                {
                    delta = Math.Max(delta, Math.Abs(actual[i + c] - expected[i + c]));
                }

                maxDelta = Math.Max(maxDelta, delta);
                var grey = (byte)(((actual[i] * 54) + (actual[i + 1] * 183) + (actual[i + 2] * 19)) >> 10);
                if (delta > channel)
                {
                    changed++;
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x + 1);
                    bottom = Math.Max(bottom, y + 1);
                    image[i] = 255;
                    image[i + 1] = grey;
                    image[i + 2] = grey;
                }
                else
                {
                    image[i] = grey;
                    image[i + 1] = grey;
                    image[i + 2] = grey;
                }

                image[i + 3] = 255;
            }
        }

        diff = image;
        var box = changed == 0 ? default : new Box(left, top, right - left, bottom - top);
        return new ImageComparison(SameSize: true, changed, maxDelta, box);
    }
}
