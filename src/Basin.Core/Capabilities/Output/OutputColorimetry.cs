namespace Basin.Capabilities;

public readonly record struct OutputColorimetry
{
    public double MaxLuminance { get; init; }

    public double MaxFrameAverageLuminance { get; init; }

    public double MinLuminance { get; init; }

    public (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)? Chromaticities { get; init; }

    public bool SupportsPq { get; init; }

    public bool SupportsBt2020 { get; init; }
}
