namespace Basin.Scene3D;

/// <summary>
/// The part of a <see cref="WorldWindowFrame"/> that a pick hit, from <see cref="WorldWindowFrame.PartAt"/>.
/// </summary>
public enum WorldFramePart
{
    /// <summary>
    /// No frame part. The hit was not on a frame.
    /// </summary>
    None,
    /// <summary>
    /// The outer part of a rail, or the frame's outer side.
    /// </summary>
    Face,
    /// <summary>
    /// The inner lip of the left rail.
    /// </summary>
    Left,
    /// <summary>
    /// The inner lip of the right rail.
    /// </summary>
    Right,
    /// <summary>
    /// The inner lip of the top rail.
    /// </summary>
    Top,
    /// <summary>
    /// The inner lip of the bottom rail.
    /// </summary>
    Bottom,
    /// <summary>
    /// The top-left corner block.
    /// </summary>
    TopLeft,
    /// <summary>
    /// The top-right corner block.
    /// </summary>
    TopRight,
    /// <summary>
    /// The bottom-left corner block.
    /// </summary>
    BottomLeft,
    /// <summary>
    /// The bottom-right corner block.
    /// </summary>
    BottomRight,
}
