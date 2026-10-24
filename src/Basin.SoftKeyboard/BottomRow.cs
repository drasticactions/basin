namespace Basin.SoftKeyboard;

/// <summary>
/// Selects the bottom-row variant of a layout for the kind of the focused field.
/// </summary>
public enum BottomRow
{
    /// <summary>
    /// The bottom row for a text field or for no field.
    /// </summary>
    Default,
    /// <summary>
    /// The bottom row for an email field, with the keys that an address needs.
    /// </summary>
    Email,
    /// <summary>
    /// The bottom row for a URL field, with the keys that an address needs.
    /// </summary>
    Url,
}
