using Basin.Capabilities;

namespace Basin.Color;

public sealed record OutputColorState
{
    public bool HighDynamicRange { get; init; }

    public bool WideColorGamut { get; init; }

    public uint SdrBrightnessNits { get; init; } = 200;

    public uint SdrGamutWideness { get; init; }

    public OutputColorProfileSource Source { get; init; } = OutputColorProfileSource.Srgb;

    public string? IccProfilePath { get; init; }

    public OutputColorProfileSource HdrSource { get; init; } = OutputColorProfileSource.Srgb;

    public string? HdrIccProfilePath { get; init; }

    public OutputBrightnessOverrides BrightnessOverrides { get; init; } = OutputBrightnessOverrides.None;

    public bool DdcCiAllowed { get; init; } = true;

    public OutputColorPowerTradeoff ColorPowerTradeoff { get; init; } = OutputColorPowerTradeoff.Efficiency;

    public OutputEdrPolicy EdrPolicy { get; init; } = OutputEdrPolicy.Never;

    public uint Brightness { get; init; } = 10000;

    public uint Dimming { get; init; } = 10000;
}
