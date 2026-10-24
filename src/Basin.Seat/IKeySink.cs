namespace Basin.Seat;

/// <summary>
/// Receives synthesized key events, for example from a <see cref="KeySynthesizer"/>.
/// </summary>
public interface IKeySink
{
    /// <summary>
    /// Delivers a key press or release.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    void NotifyKey(uint timeMs, uint key, bool pressed);

    /// <summary>
    /// Commits text that the keymap cannot type, for example through an input method. The default implementation returns <see langword="false"/>.
    /// </summary>
    /// <param name="text">The text to commit.</param>
    /// <returns><see langword="true"/> when the text was committed.</returns>
    bool TryCommitText(ReadOnlySpan<char> text) => false;
}
