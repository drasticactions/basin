namespace Basin.Seat;

/// <summary>
/// Receives the multi-finger swipe that a <see cref="CentroidSwipeGesture"/> recognizes.
/// </summary>
public interface ICentroidSwipeHandler
{
    /// <summary>
    /// Called once when the fingers travel past the slop. Return <see langword="false"/> to let the touches go to their usual targets.
    /// </summary>
    /// <param name="centroidX">The horizontal center of the contacts, in the router's coordinate space.</param>
    /// <param name="centroidY">The vertical center of the contacts, in the router's coordinate space.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns><see langword="true"/> to claim the touches for the swipe.</returns>
    bool Begin(double centroidX, double centroidY, uint timeMs);

    /// <summary>
    /// Called for each contact motion while the swipe is claimed. The first call comes right after <see cref="Begin"/> and carries the travel along the axis that passed the slop.
    /// </summary>
    /// <param name="dx">The horizontal move of the contact center since the previous event.</param>
    /// <param name="dy">The vertical move of the contact center since the previous event.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void Update(double dx, double dy, uint timeMs);

    /// <summary>
    /// Called once when the first finger lifts, or when the gesture is cancelled.
    /// </summary>
    /// <param name="cancelled"><see langword="true"/> when the router cancelled the gesture. The time is then zero.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void End(bool cancelled, uint timeMs);
}
