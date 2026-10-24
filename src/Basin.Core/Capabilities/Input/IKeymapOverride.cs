namespace Basin.Capabilities;

/// <summary>
/// Changes the keysyms of some keys for a short time, for example to type a character that the layout does not have.
/// </summary>
public interface IKeymapOverride
{
    /// <summary>
    /// Applies a keymap in which each listed evdev keycode produces its keysym. Clients receive the new keymap.
    /// </summary>
    /// <param name="keys">The keycode and keysym pairs to apply.</param>
    /// <returns>
    /// A scope that restores the previous keymap and modifier state when disposed.
    /// It is <see langword="null"/> when nothing changed, for example when <paramref name="keys"/> is empty or no keymap is active.
    /// </returns>
    IDisposable? OverrideKeysyms(ReadOnlySpan<(uint Keycode, uint Keysym)> keys);
}
