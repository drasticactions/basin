namespace Basin.Seat;

/// <summary>
/// The compositor's pointer path that a <see cref="TouchPointerDriver"/> drives to emulate a mouse from a touch.
/// </summary>
public interface ITouchPointerTarget
{
    /// <summary>
    /// Moves the pointer to a layout position, as the compositor's own motion path does.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    void Warp(uint timeMs, double x, double y);

    /// <summary>
    /// Presses or releases a pointer button, as the compositor's own button path does.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The Linux evdev button code. The driver always sends <c>BTN_LEFT</c>.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    void Button(uint timeMs, uint button, bool pressed);
}
