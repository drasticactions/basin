using Basin.Capabilities;

namespace Basin.Color;

/// <summary>
/// A transfer function and the luminances that scale it. It converts between a signal value and
/// luminance in nits.
/// </summary>
public readonly struct TransferCharacteristics
{
    private readonly TransferKind _kind;
    private readonly double _gamma;

    /// <summary>
    /// Gets the shape of the transfer function.
    /// </summary>
    public TransferKind Kind => _kind;

    /// <summary>
    /// Gets the power exponent. It is 1 for <see cref="TransferKind.Linear"/> and 0 for the curves that
    /// have no single exponent.
    /// </summary>
    public double Gamma => _gamma;

    /// <summary>
    /// Gets the highest signal value that <see cref="Decode"/> accepts. It is above 1 only for an
    /// extended linear signal.
    /// </summary>
    public double SignalCeiling { get; }

    /// <summary>
    /// Gets the single value that a shader needs for this curve: the signal ceiling for a linear curve,
    /// otherwise <see cref="Gamma"/>.
    /// </summary>
    public double ShaderParameter => _kind == TransferKind.Linear ? SignalCeiling : _gamma;

    /// <summary>
    /// Gets the luminance of reference white in nits.
    /// </summary>
    public double ReferenceLuminance { get; }

    /// <summary>
    /// Gets the luminance in nits of signal value 1. For PQ it is the content peak, which the curve
    /// does not use to decode.
    /// </summary>
    public double MaxLuminance { get; }

    private TransferCharacteristics(TransferKind kind, double gamma, double reference, double max, double ceiling = 1)
    {
        _kind = kind;
        _gamma = gamma;
        ReferenceLuminance = reference;
        MaxLuminance = max;
        SignalCeiling = ceiling;
    }

    /// <summary>
    /// Gets the luminance in nits of the highest signal, which is <see cref="MaxLuminance"/> times <see
    /// cref="SignalCeiling"/>.
    /// </summary>
    public double PeakLuminance => MaxLuminance * SignalCeiling;

    /// <summary>
    /// Gets a value indicating whether the curve cannot go above <see cref="MaxLuminance"/>. PQ, HLG
    /// and a linear signal above 1 are not standard range.
    /// </summary>
    public bool IsStandardRange =>
        _kind is TransferKind.Compound24 or TransferKind.Gamma || (_kind == TransferKind.Linear && SignalCeiling <= 1);

    /// <summary>
    /// Reads the transfer function and luminances of an image description. Missing luminances take the
    /// defaults of the transfer function.
    /// </summary>
    /// <param name="description">The description to read. A power exponent wins over a named transfer function.</param>
    /// <returns>The characteristics. An unknown named transfer function gives the sRGB curve.</returns>
    public static TransferCharacteristics From(ImageDescription description)
    {
        double reference = description.Luminances is { } lum && lum.Reference > 0 ? lum.Reference : 0;
        double max = description.Luminances is { } lum2 && lum2.Max > 0 ? lum2.Max : 0;

        if (description.TransferPower is { } power)
        {
            var exponent = Math.Clamp(power / 10000.0, 1.0, 10.0);
            reference = reference > 0 ? reference : SdrReferenceLuminance;
            return new TransferCharacteristics(TransferKind.Gamma, exponent, reference, max > 0 ? max : reference);
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

                return new TransferCharacteristics(TransferKind.Pq, 0, reference, max > 0 ? max : 10000);

            case ColorTransferFunction.Hlg:
                reference = reference > 0 ? reference : PqReferenceLuminance;
                return new TransferCharacteristics(TransferKind.Hlg, 0, reference, max > 0 ? max : 1000);

            case ColorTransferFunction.ExtLinear:
                reference = reference > 0 ? reference : SdrReferenceLuminance;
                return new TransferCharacteristics(
                    TransferKind.Linear, 1, reference, max > 0 ? max : reference, Math.Max(1, description.SignalCeiling));

            case ColorTransferFunction.Gamma22:
                reference = reference > 0 ? reference : SdrReferenceLuminance;
                return new TransferCharacteristics(TransferKind.Gamma, 2.2, reference, max > 0 ? max : reference);

            case ColorTransferFunction.CompoundPower24:
            default:
                reference = reference > 0 ? reference : SdrReferenceLuminance;
                return new TransferCharacteristics(TransferKind.Compound24, 0, reference, max > 0 ? max : reference);
        }
    }

    /// <summary>
    /// The default reference white in nits for an SDR transfer function.
    /// </summary>
    public const double SdrReferenceLuminance = 80;

    /// <summary>
    /// The default reference white in nits for PQ and HLG, from BT.2408.
    /// </summary>
    public const double PqReferenceLuminance = 203;

    /// <summary>
    /// Converts a signal value into luminance. The signal is clamped to the valid range first.
    /// </summary>
    /// <param name="signal">The encoded value, from 0 to 1 or to <see cref="SignalCeiling"/>.</param>
    /// <returns>The luminance in nits.</returns>
    public double Decode(double signal)
    {
        signal = Math.Clamp(signal, 0, _kind == TransferKind.Linear ? SignalCeiling : 1);
        return _kind switch
        {
            TransferKind.Compound24 => Compound24Eotf(signal) * MaxLuminance,
            TransferKind.Gamma => Math.Pow(signal, _gamma) * MaxLuminance,
            TransferKind.Linear => signal * MaxLuminance,
            TransferKind.Pq => PqEotf(signal),
            TransferKind.Hlg => Math.Pow(HlgInverseOetf(signal), 1.2) * MaxLuminance,
            _ => signal * MaxLuminance,
        };
    }

    /// <summary>
    /// Converts a luminance into a signal value. Luminance above the curve's range is clipped.
    /// </summary>
    /// <param name="luminance">The luminance in nits. A negative value counts as 0.</param>
    /// <returns>The encoded value.</returns>
    public double Encode(double luminance)
    {
        luminance = Math.Max(0, luminance);
        return _kind switch
        {
            TransferKind.Compound24 => Compound24InverseEotf(Math.Min(1, luminance / MaxLuminance)),
            TransferKind.Gamma => Math.Pow(Math.Min(1, luminance / MaxLuminance), 1.0 / _gamma),
            TransferKind.Linear => Math.Min(SignalCeiling, luminance / MaxLuminance),
            TransferKind.Pq => PqInverseEotf(luminance),
            TransferKind.Hlg => HlgOetf(Math.Pow(Math.Min(1, luminance / MaxLuminance), 1 / 1.2)),
            _ => Math.Min(1, luminance / MaxLuminance),
        };
    }

    /// <summary>
    /// Decodes the piecewise sRGB curve.
    /// </summary>
    /// <param name="s">The signal from 0 to 1.</param>
    /// <returns>The relative linear value from 0 to 1.</returns>
    public static double Compound24Eotf(double s) =>
        s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);

    /// <summary>
    /// Encodes with the piecewise sRGB curve.
    /// </summary>
    /// <param name="l">The relative linear value from 0 to 1.</param>
    /// <returns>The signal from 0 to 1.</returns>
    public static double Compound24InverseEotf(double l) =>
        l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;

    private const double PqM1 = 2610.0 / 16384;
    private const double PqM2 = 2523.0 / 4096 * 128;
    private const double PqC1 = 3424.0 / 4096;
    private const double PqC2 = 2413.0 / 4096 * 32;
    private const double PqC3 = 2392.0 / 4096 * 32;

    /// <summary>
    /// Decodes the SMPTE ST 2084 curve.
    /// </summary>
    /// <param name="s">The signal from 0 to 1.</param>
    /// <returns>The absolute luminance in nits, from 0 to 10000.</returns>
    public static double PqEotf(double s)
    {
        var p = Math.Pow(s, 1 / PqM2);
        var num = Math.Max(p - PqC1, 0);
        return 10000 * Math.Pow(num / (PqC2 - PqC3 * p), 1 / PqM1);
    }

    /// <summary>
    /// Encodes with the SMPTE ST 2084 curve. Luminance outside 0 to 10000 nits is clamped.
    /// </summary>
    /// <param name="nits">The absolute luminance in nits.</param>
    /// <returns>The signal from 0 to 1.</returns>
    public static double PqInverseEotf(double nits)
    {
        var y = Math.Pow(Math.Clamp(nits / 10000, 0, 1), PqM1);
        return Math.Pow((PqC1 + PqC2 * y) / (1 + PqC3 * y), PqM2);
    }

    private const double HlgA = 0.17883277;
    private static readonly double HlgB = 1 - 4 * HlgA;
    private static readonly double HlgC = 0.5 - HlgA * Math.Log(4 * HlgA);

    /// <summary>
    /// Decodes the HLG curve into scene light, without the system gamma.
    /// </summary>
    /// <param name="s">The signal from 0 to 1.</param>
    /// <returns>The relative scene light from 0 to 1.</returns>
    public static double HlgInverseOetf(double s) =>
        s <= 0.5 ? s * s / 3 : (Math.Exp((s - HlgC) / HlgA) + HlgB) / 12;

    /// <summary>
    /// Encodes scene light with the HLG curve.
    /// </summary>
    /// <param name="e">The relative scene light from 0 to 1.</param>
    /// <returns>The signal from 0 to 1.</returns>
    public static double HlgOetf(double e) =>
        e <= 1.0 / 12 ? Math.Sqrt(3 * e) : HlgA * Math.Log(12 * e - HlgB) + HlgC;
}
