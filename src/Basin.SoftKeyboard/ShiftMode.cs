namespace Basin.SoftKeyboard;

/// <summary>
/// The tap state of the Shift key in <see cref="KeyboardState"/>.
/// </summary>
public enum ShiftMode
{
    /// <summary>
    /// Shift is off.
    /// </summary>
    Off,
    /// <summary>
    /// Shift applies to the next key that produces input, then turns off.
    /// </summary>
    Once,
    /// <summary>
    /// Shift stays on until the next Shift tap.
    /// </summary>
    Lock,
}
