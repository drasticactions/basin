namespace Basin.Capabilities;

/// <summary>
/// The optional properties that an output can set through <see cref="IOutputConfiguration"/>. A combined value means
/// that each named property is available. A request for a property that is not available fails.
/// </summary>
[Flags]
public enum OutputConfigurationFeatures : uint
{
    /// <summary>
    /// No optional property is available.
    /// </summary>
    None = 0,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.Overscan"/> is available.
    /// </summary>
    Overscan = 1u << 0,
    /// <summary>
    /// A variable refresh rate is available.
    /// </summary>
    Vrr = 1u << 1,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.RgbRange"/> is available.
    /// </summary>
    RgbRange = 1u << 2,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.HighDynamicRange"/> is available.
    /// </summary>
    HighDynamicRange = 1u << 3,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.WideColorGamut"/> is available. No configuration in basin advertises it, because none applies it.
    /// </summary>
    WideColorGamut = 1u << 4,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.AutoRotate"/> is available, because an orientation sensor is present.
    /// </summary>
    AutoRotate = 1u << 5,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.IccProfilePath"/> is available.
    /// </summary>
    IccProfile = 1u << 6,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.Brightness"/> is available.
    /// </summary>
    Brightness = 1u << 7,
    /// <summary>
    /// The display has a built-in color profile, so <see cref="OutputColorProfileSource.Edid"/> is available.
    /// </summary>
    BuiltInColor = 1u << 8,
    /// <summary>
    /// Brightness control over DDC/CI is available.
    /// </summary>
    DdcCi = 1u << 9,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.MaxBitsPerColor"/> is available.
    /// </summary>
    MaxBitsPerColor = 1u << 10,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.EdrPolicy"/> is available.
    /// </summary>
    Edr = 1u << 11,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.Sharpness"/> is available.
    /// </summary>
    Sharpness = 1u << 12,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.CustomModes"/> is available.
    /// </summary>
    CustomModes = 1u << 13,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.AutoBrightness"/> is available.
    /// </summary>
    AutoBrightness = 1u << 14,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.HdrIccProfilePath"/> is available. No configuration in basin advertises it, because none applies it.
    /// </summary>
    HdrIccProfile = 1u << 15,
    /// <summary>
    /// <see cref="OutputConfigurationEntry.AbmLevel"/> is available.
    /// </summary>
    AbmLevel = 1u << 16,
}
