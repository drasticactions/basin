namespace Basin;

/// <summary>
/// Holds a 3D color lookup table for a renderer that declares <see cref="ColorTransformCapability.Lut3D"/>. The table maps straight-alpha, display-encoded RGB in [0, 1] to output RGB.
/// </summary>
public sealed class ColorLut3D
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ColorLut3D"/> class. It throws <see cref="ArgumentOutOfRangeException"/> for a size under 2, and <see cref="ArgumentException"/> when the data length does not match the size.
    /// </summary>
    /// <param name="size">The number of grid points on each axis.</param>
    /// <param name="data">The RGB triplets of the grid, red axis fastest, then green, then blue. The table keeps the array without a copy.</param>
    public ColorLut3D(int size, float[] data)
    {
        if (size < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "A LUT needs at least 2 grid points on each axis.");
        }

        if (data.Length != size * size * size * 3)
        {
            throw new ArgumentException($"The data has {data.Length} floats. A {size}³ RGB grid needs {size * size * size * 3} floats.", nameof(data));
        }

        Size = size;
        Data = data;
    }

    /// <summary>
    /// Gets the number of grid points on each axis.
    /// </summary>
    public int Size { get; }

    /// <summary>
    /// Gets the RGB triplets of the grid, with the red axis fastest. The index of a channel is <c>(((b * Size) + g) * Size + r) * 3 + channel</c>.
    /// </summary>
    public float[] Data { get; }

    /// <summary>
    /// Gets the luminance in cd/m² that an input signal of 1 stands for. When it is above zero, each input goes through the inverse PQ curve before the lookup. Zero means no shaper.
    /// </summary>
    public float ShaperNits { get; init; }

    /// <summary>
    /// Gets the largest input signal that the table was baked for, for example 125 for scRGB content. The default is 1.
    /// </summary>
    public float ShaperInputMax { get; init; } = 1;

    /// <summary>
    /// Applies the input shaper to one channel.
    /// </summary>
    /// <param name="signal">The input value.</param>
    /// <param name="shaperNits">The luminance that a signal of 1 stands for, or zero or less for no shaper.</param>
    /// <returns>The grid coordinate from 0 to 1. Without a shaper it is the signal clamped to [0, 1].</returns>
    public static float Shape(float signal, float shaperNits) =>
        shaperNits <= 0 ? Math.Clamp(signal, 0f, 1f) : PqInverse(signal * shaperNits);

    /// <summary>
    /// Encodes a luminance with the SMPTE ST 2084 inverse EOTF.
    /// </summary>
    /// <param name="nits">The luminance in cd/m². Values are clamped to [0, 10000].</param>
    /// <returns>The PQ signal from 0 to 1.</returns>
    public static float PqInverse(float nits)
    {
        var y = MathF.Pow(Math.Clamp(nits / 10000f, 0f, 1f), PqM1);
        return MathF.Pow((PqC1 + PqC2 * y) / (1 + PqC3 * y), PqM2);
    }

    private const float PqM1 = 2610f / 16384;
    private const float PqM2 = 2523f / 4096 * 128;
    private const float PqC1 = 3424f / 4096;
    private const float PqC2 = 2413f / 4096 * 32;
    private const float PqC3 = 2392f / 4096 * 32;

    /// <summary>
    /// Looks up a color through the input shaper and tetrahedral interpolation, as a renderer does.
    /// </summary>
    /// <param name="r">The red input.</param>
    /// <param name="g">The green input.</param>
    /// <param name="b">The blue input.</param>
    /// <returns>The output color.</returns>
    public (float R, float G, float B) SampleShaped(float r, float g, float b) =>
        SampleTetrahedral(Shape(r, ShaperNits), Shape(g, ShaperNits), Shape(b, ShaperNits));

    /// <summary>
    /// Looks up a color with tetrahedral interpolation. Inputs are clamped to [0, 1].
    /// </summary>
    /// <param name="r">The red grid coordinate.</param>
    /// <param name="g">The green grid coordinate.</param>
    /// <param name="b">The blue grid coordinate.</param>
    /// <returns>The interpolated output color.</returns>
    public (float R, float G, float B) SampleTetrahedral(float r, float g, float b)
    {
        var max = Size - 1;
        var fr = Math.Clamp(r, 0f, 1f) * max;
        var fg = Math.Clamp(g, 0f, 1f) * max;
        var fb = Math.Clamp(b, 0f, 1f) * max;
        var r0 = Math.Min((int)fr, max - 1);
        var g0 = Math.Min((int)fg, max - 1);
        var b0 = Math.Min((int)fb, max - 1);
        var x = fr - r0;
        var y = fg - g0;
        var z = fb - b0;
        Span<float> result = stackalloc float[3];
        for (var c = 0; c < 3; c++)
        {
            var c000 = At(r0, g0, b0, c);
            var c111 = At(r0 + 1, g0 + 1, b0 + 1, c);
            float value;
            if (x >= y && y >= z)
            {
                value = c000 + x * (At(r0 + 1, g0, b0, c) - c000) + y * (At(r0 + 1, g0 + 1, b0, c) - At(r0 + 1, g0, b0, c)) + z * (c111 - At(r0 + 1, g0 + 1, b0, c));
            }
            else if (x >= z && z >= y)
            {
                value = c000 + x * (At(r0 + 1, g0, b0, c) - c000) + z * (At(r0 + 1, g0, b0 + 1, c) - At(r0 + 1, g0, b0, c)) + y * (c111 - At(r0 + 1, g0, b0 + 1, c));
            }
            else if (z >= x && x >= y)
            {
                value = c000 + z * (At(r0, g0, b0 + 1, c) - c000) + x * (At(r0 + 1, g0, b0 + 1, c) - At(r0, g0, b0 + 1, c)) + y * (c111 - At(r0 + 1, g0, b0 + 1, c));
            }
            else if (y >= x && x >= z)
            {
                value = c000 + y * (At(r0, g0 + 1, b0, c) - c000) + x * (At(r0 + 1, g0 + 1, b0, c) - At(r0, g0 + 1, b0, c)) + z * (c111 - At(r0 + 1, g0 + 1, b0, c));
            }
            else if (y >= z && z >= x)
            {
                value = c000 + y * (At(r0, g0 + 1, b0, c) - c000) + z * (At(r0, g0 + 1, b0 + 1, c) - At(r0, g0 + 1, b0, c)) + x * (c111 - At(r0, g0 + 1, b0 + 1, c));
            }
            else
            {
                value = c000 + z * (At(r0, g0, b0 + 1, c) - c000) + y * (At(r0, g0 + 1, b0 + 1, c) - At(r0, g0, b0 + 1, c)) + x * (c111 - At(r0, g0 + 1, b0 + 1, c));
            }

            result[c] = value;
        }

        return (result[0], result[1], result[2]);
    }

    /// <summary>
    /// Converts a premultiplied color. It divides alpha out, looks the color up through the shaper with trilinear interpolation, and multiplies alpha back in.
    /// </summary>
    /// <param name="color">The premultiplied color to convert.</param>
    /// <returns>The converted color, premultiplied. A color with no alpha comes back unchanged.</returns>
    public RenderColor ApplyPremultiplied(in RenderColor color)
    {
        var a = Math.Clamp(color.A, 0f, 1f);
        if (a <= 0f)
        {
            return color;
        }

        var (r, g, b) = Sample(Shape(color.R / a, ShaperNits), Shape(color.G / a, ShaperNits), Shape(color.B / a, ShaperNits));
        return new RenderColor(Math.Clamp(r, 0f, 1f) * a, Math.Clamp(g, 0f, 1f) * a, Math.Clamp(b, 0f, 1f) * a, color.A);
    }

    /// <summary>
    /// Looks up a color with trilinear interpolation. Inputs are clamped to [0, 1].
    /// </summary>
    /// <param name="r">The red grid coordinate.</param>
    /// <param name="g">The green grid coordinate.</param>
    /// <param name="b">The blue grid coordinate.</param>
    /// <returns>The interpolated output color.</returns>
    public (float R, float G, float B) Sample(float r, float g, float b)
    {
        var n = Size;
        var max = n - 1;
        var fr = Math.Clamp(r, 0f, 1f) * max;
        var fg = Math.Clamp(g, 0f, 1f) * max;
        var fb = Math.Clamp(b, 0f, 1f) * max;
        var r0 = (int)fr;
        var g0 = (int)fg;
        var b0 = (int)fb;
        var r1 = Math.Min(r0 + 1, max);
        var g1 = Math.Min(g0 + 1, max);
        var b1 = Math.Min(b0 + 1, max);
        var tr = fr - r0;
        var tg = fg - g0;
        var tb = fb - b0;

        Span<float> result = stackalloc float[3];
        for (var c = 0; c < 3; c++)
        {
            var c000 = At(r0, g0, b0, c);
            var c100 = At(r1, g0, b0, c);
            var c010 = At(r0, g1, b0, c);
            var c110 = At(r1, g1, b0, c);
            var c001 = At(r0, g0, b1, c);
            var c101 = At(r1, g0, b1, c);
            var c011 = At(r0, g1, b1, c);
            var c111 = At(r1, g1, b1, c);
            var c00 = c000 + (c100 - c000) * tr;
            var c10 = c010 + (c110 - c010) * tr;
            var c01 = c001 + (c101 - c001) * tr;
            var c11 = c011 + (c111 - c011) * tr;
            var c0 = c00 + (c10 - c00) * tg;
            var c1 = c01 + (c11 - c01) * tg;
            result[c] = c0 + (c1 - c0) * tb;
        }

        return (result[0], result[1], result[2]);
    }

    private float At(int r, int g, int b, int channel) =>
        Data[(((b * Size) + g) * Size + r) * 3 + channel];
}
