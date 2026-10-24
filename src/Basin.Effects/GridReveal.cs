namespace Basin.Effects;

/// <summary>
/// Describes how much of a canvas grid is visible during a reveal animation. All positions are in the space that the grid vertices use.
/// </summary>
/// <param name="Style">The shape of the reveal.</param>
/// <param name="Progress">The reveal progress. A value of 1 or more shows the whole grid. A value of 0 or less shows nothing.</param>
/// <param name="OriginX">The horizontal origin of the reveal. A value that is not finite uses the center of the grid bounds.</param>
/// <param name="OriginY">The vertical origin of the reveal. A value that is not finite uses the center of the grid bounds.</param>
/// <param name="Feather">The width of the soft edge, in pixels. It is at least 0.001.</param>
public readonly record struct GridReveal(GridRevealStyle Style, float Progress, double OriginX, double OriginY, double Feather)
{
    /// <summary>
    /// Gets a reveal at progress 1, which draws the grid exactly as a source with no reveal does.
    /// </summary>
    public static GridReveal Full { get; } = new(GridRevealStyle.None, 1f, 0.0, 0.0, 0.0);

    /// <summary>
    /// Gets a value indicating whether <see cref="Progress"/> is 1 or more, so that the reveal shows the whole grid.
    /// </summary>
    public bool IsFull => Progress >= 1f;
}
