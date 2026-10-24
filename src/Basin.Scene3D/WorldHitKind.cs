namespace Basin.Scene3D;

/// <summary>
/// What a <see cref="WorldHit"/> hit.
/// </summary>
public enum WorldHitKind
{
    /// <summary>
    /// The ray hit nothing.
    /// </summary>
    None,
    /// <summary>
    /// The ray hit a layer that takes input. The hit names its surface.
    /// </summary>
    Surface,
    /// <summary>
    /// The ray hit a window but no input surface: a layer without input, a slab edge or back, or the canvas behind a frame opening.
    /// </summary>
    Window,
    /// <summary>
    /// The ray hit a blocker box of a room.
    /// </summary>
    Geometry,
    /// <summary>
    /// The ray hit a window's picture frame. The hit names the part.
    /// </summary>
    Frame,
    /// <summary>
    /// The ray hit a frame's plaque. The surface coordinates are plaque pixels.
    /// </summary>
    Plaque,
}
