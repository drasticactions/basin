namespace Basin.Effects;

/// <summary>
/// Names the kernel that a backdrop blur uses.
/// </summary>
public enum BlurKernel
{
    /// <summary>
    /// The dual-Kawase pyramid. The strength sets the iteration count and the offset.
    /// </summary>
    DualKawase,
    /// <summary>
    /// The kernel of the Vista DWM glass. It ignores the strength, the saturation, the contrast and the noise.
    /// </summary>
    VistaDwm,
    /// <summary>
    /// A true Gaussian with a continuous radius, at a cost that stays almost flat as the radius grows.
    /// </summary>
    SmolGaussian,
}
