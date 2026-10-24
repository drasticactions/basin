namespace Basin.Frames.Metacity;

/// <summary>
/// The GTK widget states that a <c>gtk:</c> color spec names in brackets.
/// </summary>
public enum MetacityStateFlag
{
    /// <summary>
    /// The normal state, <c>NORMAL</c>.
    /// </summary>
    Normal,
    /// <summary>
    /// The state under the pointer, <c>PRELIGHT</c>.
    /// </summary>
    Prelight,
    /// <summary>
    /// The pressed state, <c>ACTIVE</c>.
    /// </summary>
    Active,
    /// <summary>
    /// The selected state, <c>SELECTED</c>.
    /// </summary>
    Selected,
    /// <summary>
    /// The disabled state, <c>INSENSITIVE</c>.
    /// </summary>
    Insensitive,
    /// <summary>
    /// The mixed state of a check box, <c>INCONSISTENT</c>.
    /// </summary>
    Inconsistent,
    /// <summary>
    /// The state with keyboard focus, <c>FOCUSED</c>.
    /// </summary>
    Focused,
    /// <summary>
    /// The state of a widget in an unfocused window, <c>BACKDROP</c>.
    /// </summary>
    Backdrop,
}
