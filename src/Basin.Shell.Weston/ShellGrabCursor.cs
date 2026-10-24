namespace Basin.Shell.Weston;

/// <summary>
/// The cursors that <see cref="IShellClient.GrabCursor"/> can request. The values are the wire values of weston's <c>cursor</c> enum.
/// </summary>
public enum ShellGrabCursor
{
    /// <summary>
    /// No cursor image.
    /// </summary>
    None = 0,

    /// <summary>
    /// The cursor for a resize from the top edge.
    /// </summary>
    ResizeTop = 1,

    /// <summary>
    /// The cursor for a resize from the bottom edge.
    /// </summary>
    ResizeBottom = 2,

    /// <summary>
    /// The default arrow cursor.
    /// </summary>
    Arrow = 3,

    /// <summary>
    /// The cursor for a resize from the left edge.
    /// </summary>
    ResizeLeft = 4,

    /// <summary>
    /// The cursor for a resize from the top-left corner.
    /// </summary>
    ResizeTopLeft = 5,

    /// <summary>
    /// The cursor for a resize from the bottom-left corner.
    /// </summary>
    ResizeBottomLeft = 6,

    /// <summary>
    /// The cursor for an interactive move.
    /// </summary>
    Move = 7,

    /// <summary>
    /// The cursor for a resize from the right edge.
    /// </summary>
    ResizeRight = 8,

    /// <summary>
    /// The cursor for a resize from the top-right corner.
    /// </summary>
    ResizeTopRight = 9,

    /// <summary>
    /// The cursor for a resize from the bottom-right corner.
    /// </summary>
    ResizeBottomRight = 10,

    /// <summary>
    /// The cursor that shows the compositor is busy.
    /// </summary>
    Busy = 11,
}
