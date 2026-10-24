namespace Basin.Effects;

/// <summary>
/// Selects the shape of a <see cref="GridReveal"/> while its progress is below 1.
/// </summary>
public enum GridRevealStyle
{
    /// <summary>
    /// Draws nothing until the progress reaches 1.
    /// </summary>
    None,

    /// <summary>
    /// Multiplies every line color by the progress and keeps the geometry.
    /// </summary>
    Fade,

    /// <summary>
    /// Grows a disc from the origin. At progress 1 the disc reaches the farthest corner plus the feather.
    /// </summary>
    Ripple,

    /// <summary>
    /// Grows each line along its own axis from the point nearest the origin. Lines near the origin start first.
    /// </summary>
    Draw,

    /// <summary>
    /// Moves a straight front from the bounds edge nearest the origin to the opposite edge.
    /// </summary>
    Sweep,
}
