namespace Basin.Effects;

public static class BlurNoise
{
    public const int Size = 256;

    public const ulong DefaultSeed = 0x9E3779B97F4A7C15;

    public static int SizeFor(double scale) => Size * Math.Max(1, (int)Math.Round(scale));

    public static void Fill(Span<byte> pixels, int strength, double scale = 1.0, ulong seed = DefaultSeed)
    {
        var grain = Math.Max(1, (int)Math.Round(scale));
        var side = Size * grain;
        if (pixels.Length < side * side)
        {
            throw new ArgumentException($"the noise texture needs {side * side} bytes", nameof(pixels));
        }

        if (strength <= 0)
        {
            pixels[..(side * side)].Clear();
            return;
        }

        var state = seed == 0 ? DefaultSeed : seed;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                var value = (byte)(state % (ulong)strength);
                for (var dy = 0; dy < grain; dy++)
                {
                    var row = ((y * grain) + dy) * side;
                    pixels.Slice(row + (x * grain), grain).Fill(value);
                }
            }
        }
    }
}
