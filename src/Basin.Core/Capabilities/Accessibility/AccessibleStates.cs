namespace Basin.Capabilities;

/// <summary>
/// The states of an accessible node.
/// </summary>
[Flags]
public enum AccessibleStates
{
    /// <summary>
    /// No state.
    /// </summary>
    None = 0,

    /// <summary>
    /// The node has the keyboard focus.
    /// </summary>
    Focused = 1,

    /// <summary>
    /// A check box is checked or a switch is on.
    /// </summary>
    Checked = 2,

    /// <summary>
    /// A list item is selected.
    /// </summary>
    Selected = 4,

    /// <summary>
    /// The node does not respond to input.
    /// </summary>
    Disabled = 8,
}
