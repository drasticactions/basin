using Basin.Capabilities;

namespace Basin.Color;

/// <summary>
/// The color settings that <see cref="ColorOutputConfiguration"/> records for one output.
/// Brightness values in ten-thousandths use 10000 for full.
/// </summary>
public sealed record OutputColorState
{
    /// <summary>
    /// Gets a value indicating whether the output is in PQ HDR mode.
    /// </summary>
    public bool HighDynamicRange { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client asked for the wide color gamut mode. It is recorded
    /// and reported only.
    /// </summary>
    public bool WideColorGamut { get; init; }

    /// <summary>
    /// Gets the luminance in nits of SDR white while HDR is on.
    /// </summary>
    public uint SdrBrightnessNits { get; init; } = 200;

    /// <summary>
    /// Gets how far the SDR primaries move from sRGB toward the panel's native primaries, in
    /// ten-thousandths.
    /// </summary>
    public uint SdrGamutWideness { get; init; }

    /// <summary>
    /// Gets where the SDR profile comes from.
    /// </summary>
    public OutputColorProfileSource Source { get; init; } = OutputColorProfileSource.Edid;

    /// <summary>
    /// Gets the SDR ICC profile file, or <see langword="null"/>.
    /// </summary>
    public string? IccProfilePath { get; init; }

    /// <summary>
    /// Gets where the HDR profile comes from. It is recorded and reported only.
    /// </summary>
    public OutputColorProfileSource HdrSource { get; init; } = OutputColorProfileSource.Srgb;

    /// <summary>
    /// Gets the HDR ICC profile file, or <see langword="null"/>. It is validated, recorded and reported
    /// only.
    /// </summary>
    public string? HdrIccProfilePath { get; init; }

    /// <summary>
    /// Gets the luminance limits that replace the EDID values in the HDR description and metadata.
    /// </summary>
    public OutputBrightnessOverrides BrightnessOverrides { get; init; } = OutputBrightnessOverrides.None;

    /// <summary>
    /// Gets a value indicating whether a DDC/CI brightness device can drive the output. It defaults to
    /// allowed.
    /// </summary>
    public bool DdcCiAllowed { get; init; } = true;

    /// <summary>
    /// Gets whether the output prefers power efficiency or color accuracy. Accuracy turns off the
    /// panel's ABM.
    /// </summary>
    public OutputColorPowerTradeoff ColorPowerTradeoff { get; init; } = OutputColorPowerTradeoff.Efficiency;

    /// <summary>
    /// Gets when EDR can raise the backlight above the brightness setting.
    /// </summary>
    public OutputEdrPolicy EdrPolicy { get; init; } = OutputEdrPolicy.Never;

    /// <summary>
    /// Gets the brightness multiplier in ten-thousandths.
    /// </summary>
    public uint Brightness { get; init; } = 10000;

    /// <summary>
    /// Gets the dimming multiplier in ten-thousandths. It always applies as a software CTM.
    /// </summary>
    public uint Dimming { get; init; } = 10000;

    /// <summary>
    /// Gets the SDR to HDR expansion that the HDR description carries, or <see langword="null"/>.
    /// </summary>
    public InverseToneMapping? InverseToneMapping { get; init; }

    /// <summary>
    /// Gets the look table that every output description carries, or <see langword="null"/>.
    /// </summary>
    public ColorLut3D? Look { get; init; }

    /// <summary>
    /// Gets a value indicating whether HDR turns on and off with the HDR demand of the visible windows.
    /// </summary>
    public bool HdrOnDemand { get; init; }
}
