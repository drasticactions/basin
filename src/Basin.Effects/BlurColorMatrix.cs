namespace Basin.Effects;

public static class BlurColorMatrix
{
    public const int Length = 12;

    public static void Build(double saturation, double contrast, Span<float> into)
    {
        if (into.Length < Length)
        {
            throw new ArgumentException($"the colour matrix needs {Length} floats", nameof(into));
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
