namespace Basin.Capabilities;

/// <summary>
/// Reports where the keyboard focus and the text caret of the focused application are, in layout coordinates.
/// Events occur on the compositor thread.
/// </summary>
public interface IAccessibleFocus
{
    /// <summary>
    /// Occurs when the focused control or the caret moves. The box is in logical layout coordinates.
    /// </summary>
    event Action<Box, AccessibleFocusKind>? Moved;
}
