namespace Basin;

/// <summary>
/// Holds the RGB-to-YUV arithmetic that the GPU conversions implement, as a CPU reference.
/// </summary>
public static class YuvConversion
{
    /// <summary>
    /// Gets the luma weights of red and blue for a YUV matrix.
    /// </summary>
    /// <param name="coefficients">The matrix.</param>
    /// <returns>The red weight and the blue weight.</returns>
    public static (float Kr, float Kb) Weights(YuvCoefficients coefficients) => coefficients switch
    {
        YuvCoefficients.Bt601 => (0.299f, 0.114f),
        YuvCoefficients.Bt2020 => (0.2627f, 0.0593f),
        _ => (0.2126f, 0.0722f),
    };

    /// <summary>
    /// Converts one 8-bit RGB color to 8-bit YCbCr.
    /// </summary>
    /// <param name="r">The red value.</param>
    /// <param name="g">The green value.</param>
    /// <param name="b">The blue value.</param>
    /// <param name="coefficients">The YUV matrix.</param>
    /// <param name="range">The quantization range of the output.</param>
    /// <returns>The luma and the two chroma values.</returns>
    public static (byte Y, byte Cb, byte Cr) Reference(byte r, byte g, byte b, YuvCoefficients coefficients, YuvRange range)
    {
        var (kr, kb) = Weights(coefficients);
        double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
        var y = (kr * red) + ((1 - kr - kb) * green) + (kb * blue);
        var cb = (blue - y) / (2 * (1 - kb));
        var cr = (red - y) / (2 * (1 - kr));
        return range == YuvRange.Limited
            ? (Byte((16 + (219 * y)) / 255), Byte((128 + (224 * cb)) / 255), Byte((128 + (224 * cr)) / 255))
            : (Byte(y), Byte(cb + 0.5), Byte(cr + 0.5));
    }

    /// <summary>
    /// Fits a source into a target with the same aspect ratio, centered. The size and the position are even, so chroma samples do not cross the edge.
    /// </summary>
    /// <param name="sourceWidth">The source width.</param>
    /// <param name="sourceHeight">The source height.</param>
    /// <param name="targetWidth">The target width.</param>
    /// <param name="targetHeight">The target height.</param>
    /// <returns>The destination box in the target. A source with no size fills the whole target.</returns>
    public static Box Fit(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return new Box(0, 0, targetWidth, targetHeight);
        }

        var scale = Math.Min(targetWidth / (double)sourceWidth, targetHeight / (double)sourceHeight);
        var width = Math.Max(2, (int)Math.Round(sourceWidth * scale / 2) * 2);
        var height = Math.Max(2, (int)Math.Round(sourceHeight * scale / 2) * 2);
        width = Math.Min(width, targetWidth);
        height = Math.Min(height, targetHeight);
        var x = ((targetWidth - width) / 4) * 2;
        var y = ((targetHeight - height) / 4) * 2;
        return new Box(x, y, width, height);
    }

    /// <summary>
    /// Describes the two planes of a linear NV12 dmabuf as two single-plane buffers: an R8 luma plane and a half-size GR88 chroma plane. The fds are shared, not duplicated.
    /// </summary>
    /// <param name="nv12">The NV12 buffer.</param>
    /// <param name="luma">The luma plane, or the default value on failure.</param>
    /// <param name="chroma">The chroma plane, or the default value on failure.</param>
    /// <returns><see langword="false"/> when the buffer is not linear NV12 with two planes.</returns>
    public static bool TrySplit(in DmabufAttributes nv12, out DmabufAttributes luma, out DmabufAttributes chroma)
    {
        luma = default;
        chroma = default;
        if (nv12.Format != DrmFormat.Nv12 || nv12.Modifier != DrmFormatSet.ModifierLinear || nv12.PlaneCount != 2)
        {
            return false;
        }

        luma = new DmabufAttributes
        {
            Width = nv12.Width,
            Height = nv12.Height,
            Format = DrmFormat.R8,
            Modifier = DrmFormatSet.ModifierLinear,
            PlaneCount = 1,
            SamplingDevice = nv12.SamplingDevice,
        };
        luma.Fds[0] = nv12.Fds[0];
        luma.Offsets[0] = nv12.Offsets[0];
        luma.Strides[0] = nv12.Strides[0];
        chroma = luma with
        {
            Width = (nv12.Width + 1) / 2,
            Height = (nv12.Height + 1) / 2,
            Format = DrmFormat.Gr88,
        };
        chroma.Fds[0] = nv12.Fds[1];
        chroma.Offsets[0] = nv12.Offsets[1];
        chroma.Strides[0] = nv12.Strides[1];
        return true;
    }

    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
