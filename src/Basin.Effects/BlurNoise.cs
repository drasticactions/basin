namespace Basin.Effects;

/// <summary>
/// Generates the grayscale grain that masks banding in a blur.
/// </summary>
public static class BlurNoise
{
    /// <summary>
    /// The side of the noise texture in grains.
    /// </summary>
    public const int Size = 256;

    /// <summary>
    /// The xorshift seed that the blur uses when no seed is given.
    /// </summary>
    public const ulong DefaultSeed = 0x9E3779B97F4A7C15;

    /// <summary>
    /// Returns the side of the noise texture in pixels at a scale.
    /// </summary>
    /// <param name="scale">The grain size in pixels. The method rounds it to a whole number of at least 1.</param>
    /// <returns>The side length in pixels.</returns>
    public static int SizeFor(double scale) => Size * Math.Max(1, (int)Math.Round(scale));

    /// <summary>
    /// Fills a square 8-bit texture with seeded xorshift grain. The same seed always gives the same texture.
    /// </summary>
    /// <param name="pixels">The destination, at least <see cref="SizeFor"/> squared bytes long.</param>
    /// <param name="strength">The exclusive upper bound of the grain values. Zero or less clears the texture.</param>
    /// <param name="scale">The grain size in pixels.</param>
    /// <param name="seed">The xorshift seed. Zero uses <see cref="DefaultSeed"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is too short for the scale.</exception>
    public static void Fill(Span<byte> pixels, int strength, double scale = 1.0, ulong seed = DefaultSeed)
    {
        var grain = Math.Max(1, (int)Math.Round(scale));
        var side = Size * grain;
        if (pixels.Length < side * side)
        {
            throw new ArgumentException($"The span is too short. The noise texture needs {side * side} bytes.", nameof(pixels));
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
