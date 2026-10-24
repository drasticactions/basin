namespace Basin.XWayland;

/// <summary>
/// Converts an X11 scale into the DPI and the GDK window scale that X11 toolkits read.
/// </summary>
public static class XWaylandDpi
{
    /// <summary>
    /// The DPI at scale 1.
    /// </summary>
    public const int Base = 96;

    /// <summary>
    /// Computes the DPI for a scale. With buckets, it returns the bucket nearest to the exact DPI. A tie goes to the earlier bucket.
    /// </summary>
    /// <param name="scale">The X11 scale.</param>
    /// <param name="buckets">The allowed DPI values, or <see langword="null"/> or empty for the exact value.</param>
    /// <returns>The DPI to publish.</returns>
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

    /// <summary>
    /// Computes the value for <c>Gdk/WindowScalingFactor</c>.
    /// </summary>
    /// <param name="scale">The X11 scale.</param>
    /// <returns>The scale when it is a whole number of at least 1, or else 1.</returns>
    public static int WindowScalingFactor(double scale) =>
        scale >= 1 && Math.Abs(scale - Math.Round(scale)) < 1e-9 ? (int)Math.Round(scale) : 1;
}
