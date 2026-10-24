namespace Basin.Capabilities;

/// <summary>
/// The configuration of one output in an <see cref="IOutputConfiguration"/> request. A <see langword="null"/> member
/// means that the request says nothing about that property, so the current value stays. Units are the ones the
/// output-management protocols use.
/// </summary>
public readonly record struct OutputConfigurationEntry
{
    /// <summary>
    /// Gets the output that this entry configures.
    /// </summary>
    public required IOutput Output { get; init; }

    /// <summary>
    /// Gets a value indicating whether the output stays on. False disables it and removes it from the layout.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the mode to set.
    /// </summary>
    public OutputMode? Mode { get; init; }

    /// <summary>
    /// Gets the position in the layout, in logical pixels. When it is <see langword="null"/>, the compositor arranges the output itself.
    /// </summary>
    public Point? Position { get; init; }

    /// <summary>
    /// Gets the output scale.
    /// </summary>
    public double? Scale { get; init; }

    /// <summary>
    /// Gets the output transform.
    /// </summary>
    public OutputTransform? Transform { get; init; }

    /// <summary>
    /// Gets a value indicating whether to turn on a variable refresh rate. A request to turn it on fails on an output that cannot.
    /// </summary>
    public bool? AdaptiveSync { get; init; }

    /// <summary>
    /// Gets the overscan as a percentage from 0 to 100.
    /// </summary>
    public uint? Overscan { get; init; }

    /// <summary>
    /// Gets the RGB quantization range on the link.
    /// </summary>
    public OutputRgbRange? RgbRange { get; init; }

    /// <summary>
    /// Gets a value indicating whether this output is the primary output.
    /// </summary>
    public bool? Primary { get; init; }

    /// <summary>
    /// Gets the output priority, where 1 is the highest.
    /// </summary>
    public uint? Priority { get; init; }

    /// <summary>
    /// Gets a value indicating whether to drive the output in HDR mode.
    /// </summary>
    public bool? HighDynamicRange { get; init; }

    /// <summary>
    /// Gets the luminance of SDR white in HDR mode, in nits.
    /// </summary>
    public uint? SdrBrightnessNits { get; init; }

    /// <summary>
    /// Gets a value indicating whether to use the wide color gamut of the display.
    /// </summary>
    public bool? WideColorGamut { get; init; }

    /// <summary>
    /// Gets when the output follows the orientation sensor.
    /// </summary>
    public OutputAutoRotatePolicy? AutoRotate { get; init; }

    /// <summary>
    /// Gets the path of the ICC profile for SDR mode. An empty string clears the profile.
    /// </summary>
    public string? IccProfilePath { get; init; }

    /// <summary>
    /// Gets the luminance limits that replace the values from the EDID.
    /// </summary>
    public OutputBrightnessOverrides? BrightnessOverrides { get; init; }

    /// <summary>
    /// Gets how far SDR content uses the native gamut, from 0 for sRGB to 10000 for the full native gamut.
    /// </summary>
    public uint? SdrGamutWideness { get; init; }

    /// <summary>
    /// Gets where the color profile for SDR mode comes from.
    /// </summary>
    public OutputColorProfileSource? ColorProfileSource { get; init; }

    /// <summary>
    /// Gets the brightness from 0 to 10000.
    /// </summary>
    public uint? Brightness { get; init; }

    /// <summary>
    /// Gets whether the output prefers power saving or color accuracy.
    /// </summary>
    public OutputColorPowerTradeoff? ColorPowerTradeoff { get; init; }

    /// <summary>
    /// Gets a dimming factor from 0 to 10000 that multiplies the brightness. 10000 is no dimming.
    /// </summary>
    public uint? Dimming { get; init; }

    /// <summary>
    /// Gets the UUID of the output that this output mirrors. An empty string stops the mirroring.
    /// </summary>
    public string? ReplicationSourceUuid { get; init; }

    /// <summary>
    /// Gets a value indicating whether the compositor can change the display brightness over DDC/CI.
    /// </summary>
    public bool? DdcCiAllowed { get; init; }

    /// <summary>
    /// Gets the largest bit depth per color on the link. 0 leaves the choice to the driver.
    /// </summary>
    public uint? MaxBitsPerColor { get; init; }

    /// <summary>
    /// Gets when the backlight can rise above the brightness setting to show extended dynamic range content.
    /// </summary>
    public OutputEdrPolicy? EdrPolicy { get; init; }

    /// <summary>
    /// Gets the sharpness strength from 0 to 10000. 0 turns sharpening off.
    /// </summary>
    public uint? Sharpness { get; init; }

    /// <summary>
    /// Gets the custom modes to add to the modes that the output reports.
    /// </summary>
    public IReadOnlyList<OutputMode>? CustomModes { get; init; }

    /// <summary>
    /// Gets a value indicating whether the brightness follows the ambient light sensor.
    /// </summary>
    public bool? AutoBrightness { get; init; }

    /// <summary>
    /// Gets the path of the ICC profile for HDR mode. An empty string clears the profile.
    /// </summary>
    public string? HdrIccProfilePath { get; init; }

    /// <summary>
    /// Gets where the color profile for HDR mode comes from.
    /// </summary>
    public OutputColorProfileSource? HdrColorProfileSource { get; init; }

    /// <summary>
    /// Gets the adaptive backlight management level from 0 to 4. 0 turns it off.
    /// </summary>
    public uint? AbmLevel { get; init; }
}
