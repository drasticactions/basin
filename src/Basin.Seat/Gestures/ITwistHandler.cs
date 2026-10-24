namespace Basin.Seat;

/// <summary>
/// Receives the rotation that a <see cref="TwistGesture"/> recognizes.
/// </summary>
public interface ITwistHandler
{
    /// <summary>
    /// Called for each finger as it lands. The twist needs at least one accepted finger.
    /// </summary>
    /// <param name="x">The horizontal touch position.</param>
    /// <param name="y">The vertical touch position.</param>
    /// <returns><see langword="true"/> when a twist can start at this point.</returns>
    bool Accepts(double x, double y);

    /// <summary>
    /// Called once when the contacts turn more than the slop and the turn is larger than the slide.
    /// </summary>
    /// <param name="anchorX">The horizontal down position of the first accepted finger.</param>
    /// <param name="anchorY">The vertical down position of the first accepted finger.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns><see langword="true"/> to claim the touches for the twist.</returns>
    bool Begin(double anchorX, double anchorY, uint timeMs);

    /// <summary>
    /// Called for each motion while the twist is claimed, first at the claim.
    /// </summary>
    /// <param name="radians">The turn since the previous call. The first call gives the turn since the fingers landed.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void Update(double radians, uint timeMs);

    /// <summary>
    /// Called once when a finger lifts from a claimed twist, or when the router cancels it.
    /// </summary>
    /// <param name="cancelled"><see langword="true"/> when the router cancelled the gesture.</param>
    /// <param name="radiansPerSecond">The turn velocity at the release. It is zero on a cancel.</param>
    /// <param name="timeMs">The event time in milliseconds, or zero on a cancel.</param>
    void End(bool cancelled, double radiansPerSecond, uint timeMs);
}
