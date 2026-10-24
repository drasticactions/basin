namespace Basin.Capabilities;

/// <summary>
/// Changes the keysyms of some keys for a short time, for example to type a character that the layout does not have.
/// </summary>
public interface IKeymapOverride
{
    /// <summary>
    /// Applies a keymap in which each listed evdev keycode produces its keysym. The change applies to the active keymap, and clients receive it.
    /// </summary>
    /// <param name="keys">The keycode and keysym pairs to apply.</param>
    /// <returns>
    /// A scope that restores the previous active keymap and its modifier state when disposed.
    /// It is <see langword="null"/> when nothing changed, for example when <paramref name="keys"/> is empty or no keymap is active.
    /// </returns>
    IDisposable? OverrideKeysyms(ReadOnlySpan<(uint Keycode, uint Keysym)> keys);
}
