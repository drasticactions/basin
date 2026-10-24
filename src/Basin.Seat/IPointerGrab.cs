using Wayland;

namespace Basin.Seat;

/// <summary>
/// Takes the pointer events of a <see cref="SeatPointer"/> while it is the active grab, for example for a popup, a drag or an interactive move.
/// </summary>
public interface IPointerGrab
{
    /// <summary>
    /// Receives a focus change that the compositor notified.
    /// </summary>
    /// <param name="surface">The surface under the pointer, or <see langword="null"/>.</param>
    /// <param name="x">The horizontal surface-local position.</param>
    /// <param name="y">The vertical surface-local position.</param>
    void Enter(Surface? surface, double x, double y);

    /// <summary>
    /// Receives a motion that the compositor notified.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal surface-local position.</param>
    /// <param name="y">The vertical surface-local position.</param>
    void Motion(uint timeMs, double x, double y);

    /// <summary>
    /// Receives a button event that the compositor notified.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The Linux evdev button code.</param>
    /// <param name="state">The button state.</param>
    /// <returns>The serial of the delivered event, or zero when the grab delivered nothing.</returns>
    uint Button(uint timeMs, uint button, WlPointer.ButtonState state);

    /// <summary>
    /// Receives a scroll event that the compositor notified.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The scroll event.</param>
    void Axis(uint timeMs, in PointerAxis axis);

    /// <summary>
    /// Tells the grab that <see cref="SeatPointer.EndGrab"/> removed it while it was active.
    /// </summary>
    void Cancel();
}
