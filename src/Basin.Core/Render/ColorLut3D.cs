namespace Basin;

public sealed class ColorLut3D
{
    public ColorLut3D(int size, float[] data)
    {
        if (size < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "a LUT needs at least 2 grid points per axis");
        }

        if (data.Length != size * size * size * 3)
        {
            throw new ArgumentException($"expected {size * size * size * 3} floats for a {size}³ RGB grid, got {data.Length}");
        }

        Size = size;
        Data = data;
    }

    public int Size { get; }

    public float[] Data { get; }

    public float ShaperNits { get; init; }

    public float ShaperInputMax { get; init; } = 1;

    public static float Shape(float signal, float shaperNits) =>
        shaperNits <= 0 ? Math.Clamp(signal, 0f, 1f) : PqInverse(signal * shaperNits);

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

    public (float R, float G, float B) SampleShaped(float r, float g, float b) =>
        SampleTetrahedral(Shape(r, ShaperNits), Shape(g, ShaperNits), Shape(b, ShaperNits));

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
