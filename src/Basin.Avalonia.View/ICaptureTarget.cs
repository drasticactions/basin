namespace Basin.Avalonia;

/// <summary>
/// A host control or window that forwards keys to a compositor surface and can capture the pointer for it.
/// </summary>
public interface ICaptureTarget
{
    /// <summary>
    /// Gets or sets a filter that sees each host key before the compositor does.
    /// It gets the evdev keycode and <see langword="true"/> for a press. A <see langword="true"/> result consumes the key.
    /// </summary>
    Func<uint, bool, bool>? KeyFilter { get; set; }

    /// <summary>
    /// Sends a key to the compositor as if the host delivered it. Keycode 0, a second press and a release of a key that is up are ignored.
    /// </summary>
    /// <param name="code">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    void InjectKey(uint code, bool pressed);

    /// <summary>
    /// Activates or deactivates the pointer constraint of the surface that has pointer focus.
    /// </summary>
    /// <param name="captured"><see langword="true"/> to lock or confine the pointer, <see langword="false"/> to release it.</param>
    void CaptureInput(bool captured);
}
