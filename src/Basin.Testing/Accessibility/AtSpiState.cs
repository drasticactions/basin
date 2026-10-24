namespace Basin.Testing;

/// <summary>
/// The AT-SPI states that tests read. The values are the AT-SPI state numbers.
/// </summary>
public enum AtSpiState
{
    /// <summary>
    /// The window is the active one.
    /// </summary>
    Active = 1,

    /// <summary>
    /// The object is checked.
    /// </summary>
    Checked = 4,

    /// <summary>
    /// The object is enabled.
    /// </summary>
    Enabled = 8,

    /// <summary>
    /// The object can take the keyboard focus.
    /// </summary>
    Focusable = 11,

    /// <summary>
    /// The object has the keyboard focus.
    /// </summary>
    Focused = 12,

    /// <summary>
    /// The object is selected.
    /// </summary>
    Selected = 23,

    /// <summary>
    /// The object responds to input.
    /// </summary>
    Sensitive = 24,

    /// <summary>
    /// The object is on the screen.
    /// </summary>
    Showing = 25,

    /// <summary>
    /// The object is visible.
    /// </summary>
    Visible = 30,
}
