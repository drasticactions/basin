namespace Basin.Capabilities;

/// <summary>
/// Receives the key events that a <see cref="TextTyper"/> sends.
/// </summary>
public interface ITextKeyTarget
{
    /// <summary>Presses or releases a key.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <returns><see langword="true"/> when the key event was delivered.</returns>
    bool Key(uint timeMs, uint keycode, bool pressed);
}
