namespace Basin.SoftKeyboard;

/// <summary>
/// The kind of the most recent user input that <see cref="SoftKeyboardSession"/> saw.
/// </summary>
public enum LastInput
{
    /// <summary>
    /// A pointer motion or button.
    /// </summary>
    Pointer,
    /// <summary>
    /// A touch, on a client or on the soft keyboard.
    /// </summary>
    Touch,
    /// <summary>
    /// A key press on a hardware keyboard.
    /// </summary>
    Keyboard,
}
