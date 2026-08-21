using Basin.Capabilities;

namespace Basin.Color;

public readonly struct TransferCharacteristics
{
    private enum Kind
    {
        Compound24,
        Gamma,
        Linear,
        Pq,
        Hlg,
    }

    private readonly Kind _kind;
    private readonly double _gamma;

    public double ReferenceLuminance { get; }

    public double MaxLuminance { get; }

    private TransferCharacteristics(Kind kind, double gamma, double reference, double max)
    {
        _kind = kind;
        _gamma = gamma;
        ReferenceLuminance = reference;
        MaxLuminance = max;
    }

    public static TransferCharacteristics From(ImageDescription description)
    {
        double reference = description.Luminances is { } lum && lum.Reference > 0 ? lum.Reference : 0;
        double max = description.Luminances is { } lum2 && lum2.Max > 0 ? lum2.Max : 0;

        if (description.TransferPower is { } power)
        {
            var exponent = Math.Clamp(power / 10000.0, 1.0, 10.0);
            reference = reference > 0 ? reference : SdrReferenceLuminance;
            return new TransferCharacteristics(Kind.Gamma, exponent, reference, max > 0 ? max : reference);
        }

        switch (description.TransferNamed)
        {
            case ColorTransferFunction.St2084Pq:
                reference = reference > 0 ? reference : PqReferenceLuminance;
                if (max <= 0 && description.MasteringLuminance is { } ml && ml.Max > 0)
                {
                    max = ml.Max;
                }

                if (max <= 0 && description.MaxCll is { } cll && cll > 0)
                {
                    max = cll;
                }

                return new TransferCharacteristics(Kind.Pq, 0, reference, max > 0 ? max : 10000);

            case ColorTransferFunction.Hlg:
                reference = reference > 0 ? reference : PqReferenceLuminance;
                return new TransferCharacteristics(Kind.Hlg, 0, reference, max > 0 ? max : 1000);

            case ColorTransferFunction.ExtLinear:
                reference = reference > 0 ? reference : SdrReferenceLuminance;
                return new TransferCharacteristics(Kind.Linear, 1, reference, max > 0 ? max : reference);

            case ColorTransferFunction.Gamma22:
                reference = reference > 0 ? reference : SdrReferenceLuminance;
                return new TransferCharacteristics(Kind.Gamma, 2.2, reference, max > 0 ? max : reference);

            case ColorTransferFunction.CompoundPower24:
            default:
                reference = reference > 0 ? reference : SdrReferenceLuminance;
                return new TransferCharacteristics(Kind.Compound24, 0, reference, max > 0 ? max : reference);
        }
    }

    public const double SdrReferenceLuminance = 80;

    public const double PqReferenceLuminance = 203;

    public double Decode(double signal)
    {
        signal = Math.Clamp(signal, 0, 1);
        return _kind switch
        {
            Kind.Compound24 => Compound24Eotf(signal) * ReferenceLuminance,
            Kind.Gamma => Math.Pow(signal, _gamma) * ReferenceLuminance,
            Kind.Linear => signal * ReferenceLuminance,
            Kind.Pq => PqEotf(signal),
            Kind.Hlg => Math.Pow(HlgInverseOetf(signal), 1.2) * MaxLuminance,
            _ => signal * ReferenceLuminance,
        };
    }

    public double Encode(double luminance)
    {
        luminance = Math.Max(0, luminance);
        return _kind switch
        {
            Kind.Compound24 => Compound24InverseEotf(Math.Min(1, luminance / ReferenceLuminance)),
            Kind.Gamma => Math.Pow(Math.Min(1, luminance / ReferenceLuminance), 1.0 / _gamma),
            Kind.Linear => Math.Min(1, luminance / ReferenceLuminance),
            Kind.Pq => PqInverseEotf(luminance),
            Kind.Hlg => HlgOetf(Math.Pow(Math.Min(1, luminance / MaxLuminance), 1 / 1.2)),
            _ => Math.Min(1, luminance / ReferenceLuminance),
        };
    }

    public static double Compound24Eotf(double s) =>
        s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);

    public static double Compound24InverseEotf(double l) =>
        l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;

    private const double PqM1 = 2610.0 / 16384;
    private const double PqM2 = 2523.0 / 4096 * 128;
    private const double PqC1 = 3424.0 / 4096;
    private const double PqC2 = 2413.0 / 4096 * 32;
    private const double PqC3 = 2392.0 / 4096 * 32;

    public static double PqEotf(double s)
    {
        var p = Math.Pow(s, 1 / PqM2);
        var num = Math.Max(p - PqC1, 0);
        return 10000 * Math.Pow(num / (PqC2 - PqC3 * p), 1 / PqM1);
    }

    public static double PqInverseEotf(double nits)
    {
        var y = Math.Pow(Math.Clamp(nits / 10000, 0, 1), PqM1);
        return Math.Pow((PqC1 + PqC2 * y) / (1 + PqC3 * y), PqM2);
    }

    private const double HlgA = 0.17883277;
    private static readonly double HlgB = 1 - 4 * HlgA;
    private static readonly double HlgC = 0.5 - HlgA * Math.Log(4 * HlgA);

    public static double HlgInverseOetf(double s) =>
        s <= 0.5 ? s * s / 3 : (Math.Exp((s - HlgC) / HlgA) + HlgB) / 12;

    public static double HlgOetf(double e) =>
        e <= 1.0 / 12 ? Math.Sqrt(3 * e) : HlgA * Math.Log(12 * e - HlgB) + HlgC;
}
