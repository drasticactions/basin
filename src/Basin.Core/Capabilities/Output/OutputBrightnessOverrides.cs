namespace Basin.Capabilities;

/// <summary>
/// Luminance limits that replace the values from the EDID. A negative value keeps the EDID value.
/// </summary>
/// <param name="MaxPeakBrightness">The peak luminance in nits.</param>
/// <param name="MaxFrameAverageBrightness">The largest frame-average luminance in nits.</param>
/// <param name="MinBrightness">The minimum luminance in units of 0.0001 nits.</param>
public readonly record struct OutputBrightnessOverrides(
    int MaxPeakBrightness,
    int MaxFrameAverageBrightness,
    int MinBrightness)
{
    /// <summary>
    /// Gets a value that overrides no limit.
    /// </summary>
    public static OutputBrightnessOverrides None => new(-1, -1, -1);
}
