namespace Basin.Effects;

/// <summary>
/// Names the output edges that have a step. Combine the values to name more than one edge.
/// </summary>
[Flags]
public enum CanvasStepSides
{
    /// <summary>
    /// No edge.
    /// </summary>
    None = 0,
    /// <summary>
    /// The left edge.
    /// </summary>
    Left = 1,
    /// <summary>
    /// The right edge.
    /// </summary>
    Right = 2,
    /// <summary>
    /// The top edge.
    /// </summary>
    Top = 4,
    /// <summary>
    /// The bottom edge.
    /// </summary>
    Bottom = 8,
    /// <summary>
    /// The left and the right edges.
    /// </summary>
    Horizontal = Left | Right,
    /// <summary>
    /// The top and the bottom edges.
    /// </summary>
    Vertical = Top | Bottom,
    /// <summary>
    /// All four edges.
    /// </summary>
    All = Horizontal | Vertical,
}
