namespace Basin.SoftKeyboard;

/// <summary>
/// The key sink that <see cref="KeyboardRouter"/> uses when no backend and no text input takes a key.
/// </summary>
public interface ISoftKeyOutput
{
    /// <summary>
    /// Sends a press and a release of one key.
    /// </summary>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void Key(uint keycode, uint timeMs);

    /// <summary>
    /// Sends a press and a release of one key with the Control modifier set for that key only. A word delete uses it.
    /// </summary>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void ControlKey(uint keycode, uint timeMs);

    /// <summary>
    /// Sends a press and a release of one key with extra modifier bits set for that key only. The previous modifier state is restored after it.
    /// </summary>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="xkbModifiers">The xkb modifier mask that the keymap needs to produce the key's symbol.</param>
    /// <param name="modifiers">The chord modifiers to add to the mask.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void ModifiedKey(uint keycode, uint xkbModifiers, KeyModifiers modifiers, uint timeMs);

    /// <summary>
    /// Types a string as keys or as one text commit.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns>The number of characters that the output delivered.</returns>
    int Type(string text, uint timeMs);

    /// <summary>
    /// Finds the key and the modifiers that produce a keysym in the current keymap.
    /// </summary>
    /// <param name="keysym">The xkb keysym to find.</param>
    /// <param name="keycode">The evdev keycode when the method returns <see langword="true"/>.</param>
    /// <param name="modifiers">The xkb modifier mask that the key needs when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the keymap has a key for the keysym.</returns>
    bool TryResolve(uint keysym, out uint keycode, out uint modifiers);
}
