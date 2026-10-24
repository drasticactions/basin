namespace Basin.Capabilities;

/// <summary>
/// Lists the named transfer functions, numbered as in <c>wp_color_manager_v1.transfer_function</c>.
/// </summary>
public enum ColorTransferFunction : uint
{
    /// <summary>
    /// The BT.1886 display transfer function.
    /// </summary>
    Bt1886 = 1,
    /// <summary>
    /// A pure power curve with exponent 2.2. It is the default for SDR content.
    /// </summary>
    Gamma22 = 2,
    /// <summary>
    /// A pure power curve with exponent 2.8.
    /// </summary>
    Gamma28 = 3,
    /// <summary>
    /// The SMPTE ST 240 transfer function.
    /// </summary>
    St240 = 4,
    /// <summary>
    /// Linear light with an extended range above 1.
    /// </summary>
    ExtLinear = 5,
    /// <summary>
    /// A logarithmic curve over a range of 100 to 1.
    /// </summary>
    Log100 = 6,
    /// <summary>
    /// A logarithmic curve over a range of 316.22777 to 1.
    /// </summary>
    Log316 = 7,
    /// <summary>
    /// The IEC 61966-2-4 (xvYCC) transfer function.
    /// </summary>
    XvYcc = 8,
    /// <summary>
    /// The sRGB piecewise curve. The protocol deprecates it.
    /// </summary>
    Srgb = 9,
    /// <summary>
    /// The sRGB piecewise curve with an extended range. The protocol deprecates it.
    /// </summary>
    ExtSrgb = 10,
    /// <summary>
    /// The SMPTE ST 2084 perceptual quantizer (PQ) for HDR.
    /// </summary>
    St2084Pq = 11,
    /// <summary>
    /// The SMPTE ST 428-1 transfer function.
    /// </summary>
    St428 = 12,
    /// <summary>
    /// The hybrid log-gamma (HLG) curve from BT.2100.
    /// </summary>
    Hlg = 13,
    /// <summary>
    /// The IEC 61966-2-1 compound curve with exponent 2.4.
    /// </summary>
    CompoundPower24 = 14,
}
