namespace Basin.Effects;

internal readonly record struct SmolAxis(int Level, int Size, int Working, int Pad, double Sigma, int Taps)
{
    public const int MaxTaps = 12;

    public bool Filters => Sigma >= 0.01 && Working + (2 * Pad) > 1;

    public static SmolAxis For(double sigma, int size)
    {
        var maxLevel = 0;
        while ((1L << maxLevel) < size)
        {
            maxLevel++;
        }

        var level = 0;
        while (level < maxLevel && sigma >= 6 * ((double)size / SizeAt(size, level)))
        {
            level++;
        }

        var working = SizeAt(size, level);
        var scale = (double)size / working;
        var reduction = ((scale * scale) - 1) / 12;
        var residual = Math.Sqrt(Math.Max(0, (sigma * sigma) - reduction - ReconstructionVariance(scale))) / scale;
        var taps = Math.Min(MaxTaps, (int)Math.Ceiling(2 * residual));
        return new SmolAxis(level, size, working, level > 0 ? 1 : 0, residual, taps);
    }

    public static int SizeAt(int size, int level) => ((size - 1) >> level) + 1;

    private static double ReconstructionVariance(double scale)
    {
        if (scale == 1)
        {
            return 0;
        }

        return scale * scale * (scale > 2 ? 1.0 / 3 : scale == 2 ? 3.0 / 16 : 1.0 / 6);
    }
}
