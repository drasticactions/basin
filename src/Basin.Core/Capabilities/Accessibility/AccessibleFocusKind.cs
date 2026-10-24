namespace Basin.Capabilities;

/// <summary>
/// Tells what moved in an <see cref="IAccessibleFocus"/> report.
/// </summary>
public enum AccessibleFocusKind
{
    /// <summary>
    /// The keyboard focus went to another control.
    /// </summary>
    Focus,

    /// <summary>
    /// The text caret moved inside a control.
    /// </summary>
    Caret,
}
