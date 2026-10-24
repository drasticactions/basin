namespace Basin.Capabilities;

/// <summary>
/// Lists the color-management features that an <see cref="IColorProfileService"/> supports. A combination means that each named feature is supported.
/// </summary>
[Flags]
public enum ColorFeatures
{
    /// <summary>
    /// No feature.
    /// </summary>
    None = 0,

    /// <summary>
    /// Descriptions built from named primaries and a transfer function.
    /// </summary>
    Parametric = 1,

    /// <summary>
    /// Descriptions parsed from ICC profiles.
    /// </summary>
    IccProfiles = 2,

    /// <summary>
    /// Descriptions with explicit primary chromaticities.
    /// </summary>
    CustomPrimaries = 4,

    /// <summary>
    /// Descriptions with explicit minimum, maximum and reference luminances.
    /// </summary>
    Luminances = 8,

    /// <summary>
    /// Conversion tables between two descriptions.
    /// </summary>
    Transforms = 16,
}
