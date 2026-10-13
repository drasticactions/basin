namespace Basin.Backend.Drm;

public static class DrmModeGenerator
{
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
