namespace Basin.Seat;

/// <summary>
/// Receives the two-finger pinch that a <see cref="PinchGesture"/> recognizes.
/// </summary>
public interface IPinchHandler
{
    /// <summary>
    /// Called for each of the two fingers as it lands. The pinch needs at least one accepted finger.
    /// </summary>
    /// <param name="x">The horizontal touch position.</param>
    /// <param name="y">The vertical touch position.</param>
    /// <returns><see langword="true"/> when a pinch can start at this point.</returns>
    bool Accepts(double x, double y);

    /// <summary>
    /// Called once when the spread changes by more than the slop.
    /// </summary>
    /// <param name="centroidX">The horizontal center of the two fingers where they landed.</param>
    /// <param name="centroidY">The vertical center of the two fingers where they landed.</param>
    /// <param name="spread">The distance between the fingers where they landed.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns><see langword="true"/> to claim the touches for the pinch.</returns>
    bool Begin(double centroidX, double centroidY, double spread, uint timeMs);

    /// <summary>
    /// Called for each motion while the pinch is claimed, first at the claim.
    /// </summary>
    /// <param name="scale">The spread now divided by the spread at the start. It is not a step from the previous call.</param>
    /// <param name="centroidX">The horizontal center of the two fingers now.</param>
    /// <param name="centroidY">The vertical center of the two fingers now.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void Update(double scale, double centroidX, double centroidY, uint timeMs);

    /// <summary>
    /// Called once when a finger lifts from a claimed pinch.
    /// </summary>
    /// <param name="scalePerSecond">The rate of change of the scale at the release.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void End(double scalePerSecond, uint timeMs);

    /// <summary>
    /// Called instead of <see cref="End"/> when a third finger lands or the router cancels the gesture.
    /// </summary>
    void Cancel();
}
