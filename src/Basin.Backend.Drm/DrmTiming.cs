namespace Basin.Backend.Drm;

public readonly record struct DrmTiming(
    int ClockKhz,
    int HDisplay,
    int HSyncStart,
    int HSyncEnd,
    int HTotal,
    int VDisplay,
    int VSyncStart,
    int VSyncEnd,
    int VTotal,
    uint Flags = 0)
{
    public int RefreshMilliHz => HTotal <= 0 || VTotal <= 0
        ? 0
        : (int)Math.Round(ClockKhz * 1_000_000.0 / ((long)HTotal * VTotal));

    public int VerticalFrontPorch => VSyncStart - VDisplay;
}
