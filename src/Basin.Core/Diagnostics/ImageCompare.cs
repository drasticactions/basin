namespace Basin.Diagnostics;

/// <summary>
/// Compares two images with 4 bytes per pixel and makes a diff image.
/// </summary>
public static class ImageCompare
{
    /// <summary>
    /// Compares two tightly packed images with 4 bytes per pixel. A pixel is changed when one of its four channels differs by more than <paramref name="tolerance"/>.
    /// </summary>
    /// <param name="actual">The pixels of the image under test.</param>
    /// <param name="width">The width of the image under test.</param>
    /// <param name="height">The height of the image under test.</param>
    /// <param name="expected">The pixels of the reference image.</param>
    /// <param name="expectedWidth">The width of the reference image.</param>
    /// <param name="expectedHeight">The height of the reference image.</param>
    /// <param name="tolerance">The largest difference of one channel that counts as unchanged, from 0 to 255.</param>
    /// <param name="diff">A grey copy of the image under test, with byte 0 of each changed pixel set to 255. It is <see langword="null"/> when the sizes differ.</param>
    /// <returns>The counts and bounds of the changed pixels.</returns>
    public static ImageComparison Compare(
        ReadOnlySpan<byte> actual, int width, int height, ReadOnlySpan<byte> expected, int expectedWidth, int expectedHeight, int tolerance,
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
                if (delta > tolerance)
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
