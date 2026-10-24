namespace Basin.Effects;

/// <summary>
/// One step of the blur strength table. The fifteen steps spread over four downsample bands.
/// </summary>
/// <param name="Iterations">The number of downsample passes, from 1 to 4.</param>
/// <param name="Offset">The dual-Kawase sample offset in texels.</param>
/// <param name="ExpandSize">How far outside the region the passes can sample, in pixels.</param>
public readonly record struct BlurStrength(int Iterations, double Offset, int ExpandSize)
{
    /// <summary>
    /// The number of strength steps.
    /// </summary>
    public const int Steps = 15;

    private static readonly (double MinOffset, double MaxOffset, int ExpandSize)[] Offsets =
    [
        (1.0, 2.0, 10),
        (2.0, 3.0, 20),
        (2.0, 5.0, 50),
        (3.0, 8.0, 150),
    ];

    private static readonly BlurStrength[] Table = Build();

    /// <summary>
    /// Returns the table entry for a strength step.
    /// </summary>
    /// <param name="strength">The step. The method clamps it to the range from 1 to <see cref="Steps"/>.</param>
    /// <returns>The iteration count, offset and expand size of that step.</returns>
    public static BlurStrength For(int strength) => Table[Math.Clamp(strength, 1, Steps) - 1];

    /// <summary>
    /// Gets three times the standard deviation of the dual-Kawase passes at this step, in pixels. <see cref="BlurKernel.SmolGaussian"/> uses it as the radius.
    /// </summary>
    public double Radius
    {
        get
        {
            var h = 0.5 * Offset;
            var down = (0.5 * 0.25) + (0.5 * ((h * h) + Phase(0.5 + h)));
            var up = (4.0 / 3.0 * h * h)
                + (0.5 * ((2.0 / 12.0 * Phases(0)) + (4.0 / 12.0 * (Phases(h) + Phases(-h))) + (1.0 / 12.0 * (Phases(2 * h) + Phases(-2 * h)))));
            var variance = 0.0;
            for (var i = 0; i < Iterations; i++)
            {
                var scale = (double)(1 << i);
                variance += (down * scale * scale) + (up * 4 * scale * scale);
            }

            return 3 * Math.Sqrt(variance);
        }
    }

    private static double Phases(double offset) => Phase(0.25 + offset) + Phase(0.75 + offset);

    private static double Phase(double position)
    {
        var fraction = position - Math.Floor(position);
        return fraction * (1 - fraction);
    }

    private static BlurStrength[] Build()
    {
        var table = new List<BlurStrength>(Steps);
        var remaining = Steps;
        var offsetSum = 0.0;
        foreach (var (min, max, _) in Offsets)
        {
            offsetSum += max - min;
        }

        for (var i = 0; i < Offsets.Length; i++)
        {
            var (min, max, expand) = Offsets[i];
            var count = (int)Math.Ceiling((max - min) / offsetSum * Steps);
            remaining -= count;
            if (remaining < 0)
            {
                count += remaining;
            }

            var difference = max - min;
            for (var j = 1; j <= count; j++)
            {
                table.Add(new BlurStrength(i + 1, min + (difference / count * j), expand));
            }
        }

        return [.. table];
    }
}
