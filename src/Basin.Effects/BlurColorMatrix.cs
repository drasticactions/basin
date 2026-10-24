namespace Basin.Effects;

/// <summary>
/// Builds the color matrix that the last blur pass applies.
/// </summary>
public static class BlurColorMatrix
{
    /// <summary>
    /// The number of floats in one matrix: three rows of three factors and a translation.
    /// </summary>
    public const int Length = 12;

    /// <summary>
    /// Writes a saturation and contrast matrix as three rows of four floats. Saturation uses the Rec. 709 luma weights.
    /// </summary>
    /// <param name="saturation">The saturation factor. A value of 1 keeps the colors.</param>
    /// <param name="contrast">The contrast factor about mid gray. A value of 1 keeps the contrast.</param>
    /// <param name="into">The destination, at least <see cref="Length"/> floats long.</param>
    /// <exception cref="ArgumentException"><paramref name="into"/> is shorter than <see cref="Length"/>.</exception>
    public static void Build(double saturation, double contrast, Span<float> into)
    {
        if (into.Length < Length)
        {
            throw new ArgumentException($"the color matrix needs {Length} floats", nameof(into));
        }

        var red = (1.0 - saturation) * 0.2126;
        var green = (1.0 - saturation) * 0.7152;
        var blue = (1.0 - saturation) * 0.0722;
        Span<double> saturated =
        [
            red + saturation, red, red,
            green, green + saturation, green,
            blue, blue, blue + saturation,
        ];

        var translation = (1.0 - contrast) / 2.0;
        for (var output = 0; output < 3; output++)
        {
            for (var input = 0; input < 3; input++)
            {
                into[(output * 4) + input] = (float)(saturated[(input * 3) + output] * contrast);
            }

            into[(output * 4) + 3] = (float)translation;
        }
    }
}
