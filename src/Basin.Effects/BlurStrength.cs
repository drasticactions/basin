namespace Basin.Effects;

public readonly record struct BlurStrength(int Iterations, double Offset, int ExpandSize)
{
    public const int Steps = 15;

    private static readonly (double MinOffset, double MaxOffset, int ExpandSize)[] Offsets =
    [
        (1.0, 2.0, 10),
        (2.0, 3.0, 20),
        (2.0, 5.0, 50),
        (3.0, 8.0, 150),
    ];

    private static readonly BlurStrength[] Table = Build();

    public static BlurStrength For(int strength) => Table[Math.Clamp(strength, 1, Steps) - 1];

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
