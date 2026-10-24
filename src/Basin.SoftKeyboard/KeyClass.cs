namespace Basin.SoftKeyboard;

/// <summary>
/// The visual class of a key, which selects its face color in a view.
/// </summary>
public enum KeyClass
{
    /// <summary>
    /// A letter or Space.
    /// </summary>
    Letter,
    /// <summary>
    /// A text key that is not a letter and not in the number row.
    /// </summary>
    Punctuation,
    /// <summary>
    /// A function key, such as Shift or Backspace.
    /// </summary>
    Function,
    /// <summary>
    /// A text key in the number row.
    /// </summary>
    NumberRow,
}
