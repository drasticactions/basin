namespace Basin.Color;

/// <summary>
/// The shape of a transfer function that <see cref="TransferCharacteristics"/> can evaluate.
/// </summary>
public enum TransferKind
{
    /// <summary>
    /// The piecewise sRGB curve with a linear toe and a 2.4 power segment.
    /// </summary>
    Compound24,

    /// <summary>
    /// A pure power curve with the exponent in <see cref="TransferCharacteristics.Gamma"/>.
    /// </summary>
    Gamma,

    /// <summary>
    /// A linear signal that can go above 1 up to <see cref="TransferCharacteristics.SignalCeiling"/>.
    /// </summary>
    Linear,

    /// <summary>
    /// The SMPTE ST 2084 perceptual quantizer, which encodes absolute luminance up to 10000 nits.
    /// </summary>
    Pq,

    /// <summary>
    /// The BT.2100 hybrid log-gamma curve with a system gamma of 1.2.
    /// </summary>
    Hlg,
}
