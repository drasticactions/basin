using Pixman;

namespace Basin;

/// <summary>
/// Names the fields that an <see cref="OutputState"/> sets. A combination names each field that a commit carried.
/// </summary>
[Flags]
public enum OutputStateFields
{
    /// <summary>
    /// No field.
    /// </summary>
    None = 0,
    /// <summary>
    /// The power state.
    /// </summary>
    Enabled = 1 << 0,
    /// <summary>
    /// The mode.
    /// </summary>
    Mode = 1 << 1,
    /// <summary>
    /// The scale.
    /// </summary>
    Scale = 1 << 2,
    /// <summary>
    /// The transform.
    /// </summary>
    Transform = 1 << 3,
    /// <summary>
    /// The primary buffer.
    /// </summary>
    Buffer = 1 << 4,
    /// <summary>
    /// The damage of the primary buffer.
    /// </summary>
    Damage = 1 << 5,
    /// <summary>
    /// Variable refresh.
    /// </summary>
    AdaptiveSync = 1 << 6,
    /// <summary>
    /// The in-fence of the primary buffer.
    /// </summary>
    InFence = 1 << 7,
    /// <summary>
    /// The request for an out-fence.
    /// </summary>
    OutFence = 1 << 8,
    /// <summary>
    /// Tearing flips.
    /// </summary>
    Tearing = 1 << 9,
    /// <summary>
    /// The HDR metadata.
    /// </summary>
    Hdr = 1 << 10,
    /// <summary>
    /// The plane candidates.
    /// </summary>
    Layers = 1 << 11,
    /// <summary>
    /// The gamma lookup table.
    /// </summary>
    GammaLut = 1 << 12,
    /// <summary>
    /// The color transform matrix.
    /// </summary>
    Ctm = 1 << 13,
    /// <summary>
    /// The degamma lookup table.
    /// </summary>
    DegammaLut = 1 << 14,
    /// <summary>
    /// The RGB quantization range.
    /// </summary>
    RgbRange = 1 << 15,
    /// <summary>
    /// The bit depth limit of the link.
    /// </summary>
    MaxBitsPerColor = 1 << 16,
    /// <summary>
    /// The overscan.
    /// </summary>
    Overscan = 1 << 17,
    /// <summary>
    /// The custom modes.
    /// </summary>
    CustomModes = 1 << 18,
    /// <summary>
    /// The panel sharpness.
    /// </summary>
    Sharpness = 1 << 19,
    /// <summary>
    /// The adaptive backlight level.
    /// </summary>
    AbmLevel = 1 << 20,
    /// <summary>
    /// The content type.
    /// </summary>
    ContentType = 1 << 21,
    /// <summary>
    /// The color description of the content.
    /// </summary>
    ContentDescription = 1 << 22,
}
