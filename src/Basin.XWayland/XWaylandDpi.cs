namespace Basin.XWayland;

public static class XWaylandDpi
{
    public const int Base = 96;

    public static int For(double scale, IReadOnlyList<int>? buckets)
    {
        var exact = (int)Math.Round(Base * scale, MidpointRounding.AwayFromZero);
        if (buckets is not { Count: > 0 })
        {
            return exact;
        }

        var best = buckets[0];
        foreach (var bucket in buckets)
        {
            if (Math.Abs(bucket - exact) < Math.Abs(best - exact))
            {
                best = bucket;
            }
        }

        return best;
    }

    public static int WindowScalingFactor(double scale) =>
        scale >= 1 && Math.Abs(scale - Math.Round(scale)) < 1e-9 ? (int)Math.Round(scale) : 1;
}
