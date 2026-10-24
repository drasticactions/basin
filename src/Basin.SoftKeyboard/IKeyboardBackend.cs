namespace Basin.SoftKeyboard;

/// <summary>
/// An input-method composer that takes the soft keyboard's keys as keysyms, for example fcitx5.
/// </summary>
public interface IKeyboardBackend
{
    /// <summary>
    /// Gets a value indicating whether the backend takes keys now. While it is <see langword="true"/>, <see cref="KeyboardRouter"/> sends every key to the backend.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Sends one key press or one key release to the backend.
    /// </summary>
    /// <param name="keysym">The xkb keysym of the key.</param>
    /// <param name="keycode">The xkb keycode, which is the evdev code plus 8. It is 0 when the seat keymap has no key for the keysym.</param>
    /// <param name="state">The xkb modifier mask that applies to the key.</param>
    /// <param name="release"><see langword="true"/> for the release, <see langword="false"/> for the press.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void ProcessKey(uint keysym, uint keycode, uint state, bool release, uint timeMs);
}
