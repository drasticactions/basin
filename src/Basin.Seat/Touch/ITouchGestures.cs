namespace Basin.Seat;

/// <summary>
/// A touch gesture that a <see cref="TouchRouter"/> asks first about every contact event.
/// </summary>
public interface ITouchGestures
{
    /// <summary>
    /// Reports a new contact.
    /// </summary>
    /// <param name="id">The contact id. A synthetic contact has a negative id.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns>What the router must do with the contact.</returns>
    TouchGestureVerdict Down(int id, uint timeMs, double x, double y);

    /// <summary>
    /// Reports a contact motion.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns>What the router must do with the contact.</returns>
    TouchGestureVerdict Motion(int id, uint timeMs, double x, double y);

    /// <summary>
    /// Reports that a contact lifted.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns>What the router must do with the contact.</returns>
    TouchGestureVerdict Up(int id, uint timeMs);

    /// <summary>
    /// Drops all contacts and ends a claimed gesture as cancelled. The router calls it on a touch cancel and when another gesture claims.
    /// </summary>
    void Cancel();

    /// <summary>
    /// Moves the events that the gesture kept back into a buffer and forgets them.
    /// </summary>
    /// <param name="into">The buffer that receives the samples in event order.</param>
    /// <returns>The number of samples written. A gesture that never withholds returns zero.</returns>
    int TakeWithheld(Span<EdgeSwipeSample> into);
}
