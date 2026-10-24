namespace Basin.Capabilities;

/// <summary>
/// The color capabilities that a display reports, usually from its EDID. Luminances are in nits, and 0 means not known.
/// </summary>
public readonly record struct OutputColorimetry
{
    /// <summary>
    /// Gets the peak luminance.
    /// </summary>
    public double MaxLuminance { get; init; }

    /// <summary>
    /// Gets the largest frame-average luminance.
    /// </summary>
    public double MaxFrameAverageLuminance { get; init; }

    /// <summary>
    /// Gets the minimum luminance.
    /// </summary>
    public double MinLuminance { get; init; }

    /// <summary>
    /// Gets the CIE 1931 xy chromaticities of the red, green and blue primaries and the white point, or
    /// <see langword="null"/> when the display does not report them.
    /// </summary>
    public (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)? Chromaticities { get; init; }

    /// <summary>
    /// Gets a value indicating whether the display accepts the PQ transfer function.
    /// </summary>
    public bool SupportsPq { get; init; }

    /// <summary>
    /// Gets a value indicating whether the display accepts BT.2020 colorimetry.
    /// </summary>
    public bool SupportsBt2020 { get; init; }
}
