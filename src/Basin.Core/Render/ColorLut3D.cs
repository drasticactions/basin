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
