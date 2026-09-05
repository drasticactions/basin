namespace Basin.Capabilities;

public readonly record struct OutputConfigurationEntry
{
    public required IOutput Output { get; init; }

    public bool Enabled { get; init; }

    public OutputMode? Mode { get; init; }

    public Point? Position { get; init; }

    public double? Scale { get; init; }

    public OutputTransform? Transform { get; init; }

    public bool? AdaptiveSync { get; init; }

    public uint? Overscan { get; init; }


    public OutputRgbRange? RgbRange { get; init; }

    public bool? Primary { get; init; }

    public uint? Priority { get; init; }

    public bool? HighDynamicRange { get; init; }

    public uint? SdrBrightnessNits { get; init; }

    public bool? WideColorGamut { get; init; }

    public OutputAutoRotatePolicy? AutoRotate { get; init; }

    public string? IccProfilePath { get; init; }

    public OutputBrightnessOverrides? BrightnessOverrides { get; init; }

    public uint? SdrGamutWideness { get; init; }

    public OutputColorProfileSource? ColorProfileSource { get; init; }

    public uint? Brightness { get; init; }

    public OutputColorPowerTradeoff? ColorPowerTradeoff { get; init; }

    public uint? Dimming { get; init; }

    public string? ReplicationSourceUuid { get; init; }

    public bool? DdcCiAllowed { get; init; }

    public uint? MaxBitsPerColor { get; init; }

    public OutputEdrPolicy? EdrPolicy { get; init; }

    public uint? Sharpness { get; init; }

    public IReadOnlyList<OutputMode>? CustomModes { get; init; }

    public bool? AutoBrightness { get; init; }

    public string? HdrIccProfilePath { get; init; }

    public OutputColorProfileSource? HdrColorProfileSource { get; init; }

    public uint? AbmLevel { get; init; }
}
