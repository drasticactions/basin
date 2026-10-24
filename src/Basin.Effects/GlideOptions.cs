namespace Basin.Effects;

/// <summary>
/// Holds the shape of a <see cref="GlideEffect"/>. The defaults are KWin's.
/// </summary>
public readonly record struct GlideOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GlideOptions"/> struct with KWin's defaults.
    /// </summary>
    public GlideOptions()
    {
    }

    /// <summary>
    /// Gets the base duration in milliseconds, before the animation factor of the desktop. The default is 160.
    /// </summary>
    public double BaseMillis { get; init; } = 160;

    /// <summary>
    /// Gets the edge that an opening window turns about.
    /// </summary>
    public FrustumEdge InEdge { get; init; } = FrustumEdge.Top;

    /// <summary>
    /// Gets the start angle of an open, in degrees.
    /// </summary>
    public double InAngle { get; init; } = 3.0;

    /// <summary>
    /// Gets the start depth of an open, in the units of <see cref="Projection.Frustum"/>.
    /// </summary>
    public double InDistance { get; init; } = 30.0;

    /// <summary>
    /// Gets the start opacity of an open, from 0 to 1.
    /// </summary>
    public double InOpacity { get; init; } = 0.4;

    /// <summary>
    /// Gets the edge that a closing window turns about.
    /// </summary>
    public FrustumEdge OutEdge { get; init; } = FrustumEdge.Bottom;

    /// <summary>
    /// Gets the end angle of a close, in degrees.
    /// </summary>
    public double OutAngle { get; init; } = 3.0;

    /// <summary>
    /// Gets the end depth of a close, in the units of <see cref="Projection.Frustum"/>.
    /// </summary>
    public double OutDistance { get; init; } = 30.0;

    /// <summary>
    /// Gets the end opacity of a close, from 0 to 1.
    /// </summary>
    public double OutOpacity { get; init; } = 0.0;
}
