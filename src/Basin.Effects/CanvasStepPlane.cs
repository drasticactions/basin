namespace Basin.Effects;

/// <summary>
/// Names the plane of a step map that a point or a window lies on.
/// </summary>
public enum CanvasStepPlane
{
    /// <summary>
    /// The flat desktop in the center, drawn at the view zoom.
    /// </summary>
    Desktop,
    /// <summary>
    /// The shelf past the step, drawn at the shelf scale.
    /// </summary>
    Shelf,
}
