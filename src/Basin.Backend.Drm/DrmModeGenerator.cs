namespace Basin.Backend.Drm;

/// <summary>
/// Derives a custom mode at another refresh rate from a native timing.
/// </summary>
public static class DrmModeGenerator
{
    /// <summary>
    /// Keeps the pixel clock and horizontal timing, and changes the vertical front porch to reach the refresh rate.
    /// </summary>
    /// <param name="preferred">The native timing to start from.</param>
    /// <param name="refreshMilliHz">The wanted refresh rate in millihertz.</param>
    /// <param name="mode">The new timing, or <paramref name="preferred"/> on failure.</param>
    /// <returns><see langword="false"/> when an input is not positive or the front porch is less than one line.</returns>
    public static bool TrySameTiming(in DrmTiming preferred, int refreshMilliHz, out DrmTiming mode)
    {
        mode = preferred;
        if (refreshMilliHz <= 0 || preferred.HTotal <= 0 || preferred.ClockKhz <= 0)
        {
            return false;
        }

        var vtotal = (int)Math.Round(preferred.ClockKhz * 1_000_000.0 / ((long)preferred.HTotal * refreshMilliHz));
        var vsync = preferred.VSyncEnd - preferred.VSyncStart;
        var backPorch = preferred.VTotal - preferred.VSyncEnd;
        var frontPorch = vtotal - preferred.VDisplay - vsync - backPorch;
        if (frontPorch < 1)
        {
            return false;
        }

        var start = preferred.VDisplay + frontPorch;
        mode = preferred with { VSyncStart = start, VSyncEnd = start + vsync, VTotal = start + vsync + backPorch };
        return true;
    }

    /// <summary>
    /// Keeps every timing line and scales the pixel clock to reach the refresh rate. The clock rounds up to the next kilohertz.
    /// </summary>
    /// <param name="preferred">The native timing to start from.</param>
    /// <param name="refreshMilliHz">The wanted refresh rate in millihertz.</param>
    /// <param name="mode">The new timing, or <paramref name="preferred"/> on failure.</param>
    /// <returns><see langword="false"/> when an input is not positive.</returns>
    public static bool TryScaledClock(in DrmTiming preferred, int refreshMilliHz, out DrmTiming mode)
    {
        mode = preferred;
        if (refreshMilliHz <= 0 || preferred.HTotal <= 0 || preferred.VTotal <= 0)
        {
            return false;
        }

        var clock = (((long)preferred.HTotal * preferred.VTotal * refreshMilliHz) + 999_999) / 1_000_000;
        mode = preferred with { ClockKhz = (int)clock };
        return true;
    }
}
