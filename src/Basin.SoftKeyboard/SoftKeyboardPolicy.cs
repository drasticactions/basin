namespace Basin.SoftKeyboard;

/// <summary>
/// Selects when a field that gains the focus raises the soft keyboard automatically.
/// </summary>
public enum SoftKeyboardPolicy
{
    /// <summary>
    /// A field that activates raises the keyboard only when the last input was a touch.
    /// </summary>
    OnTouchFocus,
    /// <summary>
    /// Any field that activates raises the keyboard, whatever the last input was.
    /// </summary>
    OnAnyFocus,
}
