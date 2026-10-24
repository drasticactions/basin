namespace Basin.Seat;

/// <summary>
/// Receives the hold-then-drag gesture that a <see cref="HoldDragGesture"/> recognizes.
/// </summary>
public interface IHoldDragHandler
{
    /// <summary>
    /// Called for each finger that lands while the gesture collects contacts.
    /// </summary>
    /// <param name="x">The horizontal touch position.</param>
    /// <param name="y">The vertical touch position.</param>
    /// <returns><see langword="false"/> to stop the gesture from collecting this touch sequence.</returns>
    bool Accepts(double x, double y);

    /// <summary>
    /// Called on the first motion after the gesture claims the contacts.
    /// </summary>
    /// <param name="x">The horizontal center of the contacts where they landed.</param>
    /// <param name="y">The vertical center of the contacts where they landed.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns><see langword="false"/> to refuse the drag. The gesture then keeps the contacts and sends nothing more.</returns>
    bool Begin(double x, double y, uint timeMs);

    /// <summary>
    /// Called for each contact motion while the drag runs.
    /// </summary>
    /// <param name="x">The horizontal center of the contacts now.</param>
    /// <param name="y">The vertical center of the contacts now.</param>
    /// <param name="radians">The turn of the contacts around their center since the previous call. It is zero for one finger.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void Update(double x, double y, double radians, uint timeMs);

    /// <summary>
    /// Called once when a finger lifts, or when the gesture is cancelled, after a successful <see cref="Begin"/>.
    /// </summary>
    /// <param name="cancelled"><see langword="true"/> when the router cancelled the gesture.</param>
    /// <param name="radiansPerSecond">The turn velocity at the release. It is zero on a cancel.</param>
    /// <param name="timeMs">The event time in milliseconds, or zero on a cancel.</param>
    void End(bool cancelled, double radiansPerSecond, uint timeMs);
}
