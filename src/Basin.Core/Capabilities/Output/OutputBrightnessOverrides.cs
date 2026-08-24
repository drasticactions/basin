namespace Basin.Capabilities;

public readonly record struct OutputBrightnessOverrides(
    int MaxPeakBrightness,
    int MaxFrameAverageBrightness,
    int MinBrightness)
{
    public static OutputBrightnessOverrides None => new(-1, -1, -1);
}
