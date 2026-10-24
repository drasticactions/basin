using Basin.Capabilities;

namespace Basin.Color;

/// <summary>
/// One entry of the panel table: facts about a panel that its EDID does not give or gives wrong.
/// Every field except <see cref="Vendor"/> is optional.
/// </summary>
public sealed record PanelEntry
{
    /// <summary>
    /// Gets the three-letter EDID PNP vendor id. It matches without case.
    /// </summary>
    public required string Vendor { get; init; }

    /// <summary>
    /// Gets the EDID product code, or <see langword="null"/> to match every product.
    /// </summary>
    public int? Product { get; init; }

    /// <summary>
    /// Gets the output serial, or <see langword="null"/> to match every serial.
    /// </summary>
    public string? Serial { get; init; }

    /// <summary>
    /// Gets the transform that the panel needs to show upright, or <see langword="null"/>.
    /// </summary>
    public OutputTransform? Orientation { get; init; }

    /// <summary>
    /// Gets the CIE 1931 xy coordinates of the primaries and white point that replace the EDID ones, or
    /// <see langword="null"/>.
    /// </summary>
    public (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)? Colorimetry { get; init; }

    /// <summary>
    /// Gets the peak luminance in nits that replaces the EDID value, or <see langword="null"/>.
    /// </summary>
    public double? MaxNits { get; init; }

    /// <summary>
    /// Gets the full-frame luminance in nits that replaces the EDID value, or <see langword="null"/>.
    /// </summary>
    public double? MaxFrameAverageNits { get; init; }

    /// <summary>
    /// Gets the black level in nits that replaces the EDID value, or <see langword="null"/>.
    /// </summary>
    public double? MinNits { get; init; }

    /// <summary>
    /// Gets a value that forces HDR support on or off, or <see langword="null"/> to use the EDID.
    /// </summary>
    public bool? Hdr { get; init; }

    /// <summary>
    /// Gets the transfer function of the SDR output description, or <see langword="null"/> for the
    /// default.
    /// </summary>
    public ColorTransferFunction? OutputCurve { get; init; }

    /// <summary>
    /// Gets the custom refresh rates in millihertz to add at the preferred size. It is empty when there
    /// are none.
    /// </summary>
    public IReadOnlyList<int> RefreshMilliHz { get; init; } = [];

    /// <summary>
    /// Gets how the DRM backend makes the custom modes, or <see langword="null"/> for its default.
    /// </summary>
    public OutputModeGenerator? ModeGenerator { get; init; }

    /// <summary>
    /// Gets the highest pixel clock in kilohertz that a generated mode can use, or <see
    /// langword="null"/>.
    /// </summary>
    public int? MaxClockKhz { get; init; }

    /// <summary>
    /// Gets how many identity fields the entry names, from 1 to 3. A more specific entry wins a match.
    /// </summary>
    public int Specificity => 1 + (Product is null ? 0 : 1) + (Serial is null ? 0 : 1);

    /// <summary>
    /// Tells whether the entry applies to a panel. A field that the entry does not name matches every
    /// value.
    /// </summary>
    /// <param name="panel">The identity of the panel.</param>
    /// <returns><see langword="true"/> when the vendor and every named field match.</returns>
    public bool Matches(in PanelIdentity panel) =>
        string.Equals(Vendor, panel.Vendor, StringComparison.OrdinalIgnoreCase) &&
        (Product is null || Product == panel.Product) &&
        (Serial is null || string.Equals(Serial, panel.Serial, StringComparison.Ordinal));

    /// <summary>
    /// Puts the entry's color fields over the EDID colorimetry.
    /// </summary>
    /// <param name="edid">The colorimetry from the EDID, or <see langword="null"/> when there is none.</param>
    /// <returns>The merged colorimetry.</returns>
    public OutputColorimetry Apply(OutputColorimetry? edid)
    {
        var merged = edid ?? default;
        return merged with
        {
            Chromaticities = Colorimetry ?? merged.Chromaticities,
            MaxLuminance = MaxNits ?? merged.MaxLuminance,
            MaxFrameAverageLuminance = MaxFrameAverageNits ?? merged.MaxFrameAverageLuminance,
            MinLuminance = MinNits ?? merged.MinLuminance,
            SupportsPq = Hdr ?? merged.SupportsPq,
        };
    }
}
