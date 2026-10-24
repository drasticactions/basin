namespace Basin.Scene;

/// <summary>
/// Selects what moves the visible area of a zoomed output.
/// </summary>
[Flags]
public enum ZoomFollow
{
    /// <summary>
    /// Only explicit calls move the visible area.
    /// </summary>
    None = 0,

    /// <summary>
    /// The visible area pans when the pointer reaches its edge.
    /// </summary>
    Pointer = 1,

    /// <summary>
    /// The visible area moves to show the control with the keyboard focus.
    /// </summary>
    Focus = 2,

    /// <summary>
    /// The visible area moves to show the text caret.
    /// </summary>
    Caret = 4,

    /// <summary>
    /// The pointer, the focus and the caret all move the visible area.
    /// </summary>
    All = Pointer | Focus | Caret,
}
