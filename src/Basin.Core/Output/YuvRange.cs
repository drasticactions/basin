namespace Basin;

/// <summary>
/// Identifies the quantization range of YUV samples.
/// </summary>
public enum YuvRange
{
    /// <summary>
    /// Limited range, with luma from 16 to 235 at 8 bits.
    /// </summary>
    Limited = 0,
    /// <summary>
    /// Full range, with every code value in use.
    /// </summary>
    Full = 1,
}
