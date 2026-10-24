namespace Basin.Capabilities;

/// <summary>
/// Lists the selections that a seat holds.
/// </summary>
public enum SelectionKind
{
    /// <summary>
    /// The clipboard, set by an explicit copy.
    /// </summary>
    Clipboard,

    /// <summary>
    /// The primary selection, set by selecting text and pasted with the middle button.
    /// </summary>
    Primary,
}
