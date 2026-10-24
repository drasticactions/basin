namespace Basin.Backend.Drm;

/// <summary>
/// The timing of a display mode, in the units of a kernel mode line.
/// </summary>
/// <param name="ClockKhz">The pixel clock in kilohertz.</param>
/// <param name="HDisplay">The visible width in pixels.</param>
/// <param name="HSyncStart">The pixel where horizontal sync starts.</param>
/// <param name="HSyncEnd">The pixel where horizontal sync ends.</param>
/// <param name="HTotal">The total line length in pixels.</param>
/// <param name="VDisplay">The visible height in lines.</param>
/// <param name="VSyncStart">The line where vertical sync starts.</param>
/// <param name="VSyncEnd">The line where vertical sync ends.</param>
/// <param name="VTotal">The total frame length in lines.</param>
/// <param name="Flags">The kernel mode flags, for example sync polarity.</param>
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
    /// <summary>
    /// Gets the refresh rate in millihertz. It is 0 when a total is not positive.
    /// </summary>
    public int RefreshMilliHz => HTotal <= 0 || VTotal <= 0
        ? 0
        : (int)Math.Round(ClockKhz * 1_000_000.0 / ((long)HTotal * VTotal));

    /// <summary>
    /// Gets the number of lines between the end of the visible area and the start of vertical sync.
    /// </summary>
    public int VerticalFrontPorch => VSyncStart - VDisplay;
}
