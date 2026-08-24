namespace Basin.Capabilities;

[Flags]
public enum OutputConfigurationFeatures : uint
{
    None = 0,
    Overscan = 1u << 0,
    Vrr = 1u << 1,
    RgbRange = 1u << 2,
    HighDynamicRange = 1u << 3,
    WideColorGamut = 1u << 4,
    AutoRotate = 1u << 5,
    IccProfile = 1u << 6,
    Brightness = 1u << 7,
    BuiltInColor = 1u << 8,
    DdcCi = 1u << 9,
    MaxBitsPerColor = 1u << 10,
    Edr = 1u << 11,
    Sharpness = 1u << 12,
    CustomModes = 1u << 13,
    AutoBrightness = 1u << 14,
    HdrIccProfile = 1u << 15,
    AbmLevel = 1u << 16,
}
