using Wayland;

namespace Basin.Seat;

/// <summary>
/// Takes the touch events of a <see cref="SeatTouch"/> while it is the active grab.
/// </summary>
public interface ITouchGrab
{
    /// <summary>
    /// Receives a touch down on a surface.
    /// </summary>
    /// <param name="surface">The surface under the contact.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal surface-local position.</param>
    /// <param name="y">The vertical surface-local position.</param>
    /// <returns>The serial of the delivered down, or zero when the grab delivered nothing.</returns>
    uint Down(Surface surface, uint timeMs, int id, double x, double y);

    /// <summary>
    /// Receives the lift of a contact.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    void Up(uint timeMs, int id);

    /// <summary>
    /// Receives a contact motion.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position local to the surface of the down.</param>
    /// <param name="y">The vertical position local to the surface of the down.</param>
    void Motion(uint timeMs, int id, double x, double y);

    /// <summary>
    /// Receives the end of a group of touch events.
    /// </summary>
    void Frame();

    /// <summary>
    /// Receives a cancel of the whole touch sequence.
    /// </summary>
    void Cancel();
}
