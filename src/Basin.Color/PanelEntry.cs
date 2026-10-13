using Basin.Capabilities;

namespace Basin.Color;

public sealed record PanelEntry
{
    public required string Vendor { get; init; }

    public int? Product { get; init; }

    public string? Serial { get; init; }

    public OutputTransform? Orientation { get; init; }

    public (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)? Colorimetry { get; init; }

    public double? MaxNits { get; init; }

    public double? MaxFrameAverageNits { get; init; }

    public double? MinNits { get; init; }

    public bool? Hdr { get; init; }

    public ColorTransferFunction? OutputCurve { get; init; }

    public IReadOnlyList<int> RefreshMilliHz { get; init; } = [];

    public OutputModeGenerator? ModeGenerator { get; init; }

    public int? MaxClockKhz { get; init; }

    public int Specificity => 1 + (Product is null ? 0 : 1) + (Serial is null ? 0 : 1);

    public bool Matches(in PanelIdentity panel) =>
        string.Equals(Vendor, panel.Vendor, StringComparison.OrdinalIgnoreCase) &&
        (Product is null || Product == panel.Product) &&
        (Serial is null || string.Equals(Serial, panel.Serial, StringComparison.Ordinal));

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
