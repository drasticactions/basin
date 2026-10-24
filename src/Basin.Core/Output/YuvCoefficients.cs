namespace Basin;

/// <summary>
/// Identifies the matrix that converts YUV to RGB.
/// </summary>
public enum YuvCoefficients
{
    /// <summary>
    /// ITU-R BT.601, for standard-definition video.
    /// </summary>
    Bt601 = 0,
    /// <summary>
    /// ITU-R BT.709, for high-definition video.
    /// </summary>
    Bt709 = 1,
    /// <summary>
    /// ITU-R BT.2020 non-constant luminance, for ultra-high-definition and HDR video.
    /// </summary>
    Bt2020 = 2,
}
