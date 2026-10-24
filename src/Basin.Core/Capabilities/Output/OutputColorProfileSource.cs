namespace Basin.Capabilities;

/// <summary>
/// Where the color profile of an output comes from.
/// </summary>
public enum OutputColorProfileSource
{
    /// <summary>
    /// The output is treated as sRGB.
    /// </summary>
    Srgb = 0,
    /// <summary>
    /// The profile comes from an ICC file.
    /// </summary>
    Icc = 1,
    /// <summary>
    /// The profile comes from the colorimetry in the EDID.
    /// </summary>
    Edid = 2,
}
