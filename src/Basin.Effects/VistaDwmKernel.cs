namespace Basin.Effects;

/// <summary>
/// Holds the blur kernel traced from the Vista DWM glass. It is a 2:1 box downsample followed by 16 bilinear taps on the half-size level.
/// </summary>
public static class VistaDwmKernel
{
    /// <summary>
    /// The reach of the kernel, in full-size pixels.
    /// </summary>
    public const int ExpandSize = 8;

    /// <summary>
    /// The distance between two adjacent taps, in half-size texels.
    /// </summary>
    public const float TapSpacing = 1.216f;

    /// <summary>
    /// Gets the four tap offsets on each axis, in half-size texels from the output pixel.
    /// </summary>
    public static ReadOnlySpan<float> Offsets => [-1.824f, -0.608f, 0.608f, 1.824f];

    /// <summary>
    /// Gets the 16 tap weights in rows of four, in the order of <see cref="Offsets"/>. They sum to 1.0009 and are not symmetric, as traced.
    /// </summary>
    public static ReadOnlySpan<float> Weights =>
    [
        0.02800f, 0.04784f, 0.05251f, 0.03851f,
        0.04784f, 0.08168f, 0.09191f, 0.06534f,
        0.05251f, 0.09101f, 0.10268f, 0.07235f,
        0.03851f, 0.06534f, 0.07235f, 0.05251f,
    ];
}
