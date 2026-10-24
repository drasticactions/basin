namespace Basin.Capabilities;

/// <summary>
/// Translates between evdev keycodes and XKB keysyms with the active keymap and layout.
/// </summary>
public interface IKeymapLookup
{
    /// <summary>Finds a key and a modifier mask that produce a keysym in the active layout.</summary>
    /// <param name="keysym">The XKB keysym to find.</param>
    /// <param name="keycode">The evdev keycode of the first key that produces the keysym.</param>
    /// <param name="modifiers">The XKB modifier mask that selects the level of that keysym.</param>
    /// <returns><see langword="false"/> when no key produces the keysym or no keymap is active.</returns>
    bool TryKeycodeForKeysym(uint keysym, out uint keycode, out uint modifiers);

    /// <summary>Gets the keysym that a key produces in the current keyboard state.</summary>
    /// <param name="keycode">The evdev keycode.</param>
    /// <returns>The keysym, or 0 when the key produces none or no keymap is active.</returns>
    uint KeysymForKeycode(uint keycode);
}
