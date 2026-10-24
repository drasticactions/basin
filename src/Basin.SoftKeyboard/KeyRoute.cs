namespace Basin.SoftKeyboard;

/// <summary>
/// The path that <see cref="KeyboardRouter"/> uses to deliver a soft key.
/// </summary>
public enum KeyRoute
{
    /// <summary>
    /// The key goes to the active <see cref="IKeyboardBackend"/> as a keysym.
    /// </summary>
    Backend,
    /// <summary>
    /// Text goes to the focused text-input client as a commit string, and other keys go as keys.
    /// </summary>
    TextInput,
    /// <summary>
    /// The key goes through the <see cref="ISoftKeyOutput"/> as synthesized key events.
    /// </summary>
    Keys,
}
