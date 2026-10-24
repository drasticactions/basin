namespace Basin.Capabilities;

/// <summary>
/// Gets the text that a key press produces with the active keymap, modifiers and compose state.
/// </summary>
public interface IKeyText
{
    /// <summary>
    /// Writes the text that a press of a key produces. A call can advance the compose sequence, so call it once for each press.
    /// </summary>
    /// <param name="key">The evdev keycode.</param>
    /// <param name="into">The destination for the UTF-16 text.</param>
    /// <returns>The number of characters written. It is 0 when the key produces no text, a shortcut modifier is held, or a compose sequence is not complete.</returns>
    int TextFor(uint key, Span<char> into);
}
