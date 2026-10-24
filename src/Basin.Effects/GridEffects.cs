namespace Basin.Effects;

/// <summary>
/// Holds the glow and density settings for a canvas grid. The default value turns both off.
/// </summary>
public readonly record struct GridEffects
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GridEffects"/> struct with no glow, no density dimming and the default radii.
    /// </summary>
    public GridEffects()
    {
    }

    /// <summary>
    /// Gets settings that draw no glow and no density dimming.
    /// </summary>
    public static GridEffects None { get; } = new();

    /// <summary>
    /// Gets the glow strength at the edge of a line. Zero turns the glow off.
    /// </summary>
    public float Glow { get; init; }

    /// <summary>
    /// Gets the Gaussian radius of the glow, in screen pixels. The default is 6.
    /// </summary>
    public float GlowRadius { get; init; } = 6f;

    /// <summary>
    /// Gets a value indicating whether line alpha dims where the lines pack closer than <see cref="DensitySpacing"/>.
    /// </summary>
    public bool Density { get; init; }

    /// <summary>
    /// Gets the screen spacing, in pixels, at which density dimming starts. The default is 12.
    /// </summary>
    public float DensitySpacing { get; init; } = 12f;

    /// <summary>
    /// Gets the lowest alpha factor that density dimming applies, from 0 to 1. The default is 0.15.
    /// </summary>
    public float DensityFloor { get; init; } = 0.15f;

    /// <summary>
    /// Gets the path that draws the glow. The default is <see cref="GridGlowPath.Auto"/>.
    /// </summary>
    public GridGlowPath GlowPath { get; init; }

    /// <summary>
    /// Gets a value indicating whether a glow draws, which needs a positive <see cref="Glow"/> and <see cref="GlowRadius"/> and a path other than <see cref="GridGlowPath.None"/>.
    /// </summary>
    public bool Glows => Glow > 0f && GlowRadius > 0f && GlowPath != GridGlowPath.None;

    /// <summary>
    /// Gets a value indicating whether the halo mesh can draw the glow, which is true for <see cref="GridGlowPath.Auto"/> and <see cref="GridGlowPath.Halo"/>.
    /// </summary>
    public bool DrawsHalo => Glows && GlowPath is GridGlowPath.Auto or GridGlowPath.Halo;

    /// <summary>
    /// Calculates the alpha factor for lines at a given screen spacing.
    /// </summary>
    /// <param name="spacing">The local screen distance to the next drawn line, in pixels.</param>
    /// <returns>1 when <see cref="Density"/> is off. Otherwise the spacing over <see cref="DensitySpacing"/>, clamped between <see cref="DensityFloor"/> and 1.</returns>
    public float DensityOf(double spacing)
    {
        if (!Density)
        {
            return 1f;
        }

        var floor = Math.Clamp(DensityFloor, 0f, 1f);
        return (float)Math.Clamp(spacing / Math.Max(1e-3, DensitySpacing), floor, 1.0);
    }
}
