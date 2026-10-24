using Wayland;
using Basin.Capabilities;
using Xkb;

namespace Basin.Seat;

/// <summary>
/// Takes the keyboard events of a <see cref="SeatKeyboard"/> while it is the active grab.
/// </summary>
public interface IKeyboardGrab
{
    /// <summary>
    /// Receives a focus change, and the current focus when the grab starts.
    /// </summary>
    /// <param name="surface">The new focus, or <see langword="null"/>.</param>
    /// <param name="pressedKeys">The evdev keycodes held now, without keys that a binding consumed.</param>
    void Enter(Surface? surface, ReadOnlySpan<uint> pressedKeys);

    /// <summary>
    /// Receives a key event. The seat updates the xkb state after this call.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <param name="state">The key state.</param>
    void Key(uint timeMs, uint key, WlKeyboard.KeyState state);

    /// <summary>
    /// Receives notice that the modifier state changed. Read it from <see cref="SeatKeyboard.ModifierState"/>.
    /// </summary>
    void Modifiers();

    /// <summary>
    /// Tells the grab that <see cref="SeatKeyboard.EndGrab"/> removed it while it was active.
    /// </summary>
    void Cancel();
}
