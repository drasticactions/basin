namespace Basin.Capabilities;

/// <summary>
/// The RGB quantization range on the display link.
/// </summary>
public enum OutputRgbRange
{
    /// <summary>
    /// The driver chooses the range.
    /// </summary>
    Automatic = 0,
    /// <summary>
    /// The full range, 0 to 255 for 8 bits.
    /// </summary>
    Full = 1,
    /// <summary>
    /// The limited range, 16 to 235 for 8 bits.
    /// </summary>
    Limited = 2,
}
