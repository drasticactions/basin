namespace Basin.Seat;

/// <summary>
/// Gives an <see cref="EdgeSwipeGesture"/> its area and receives the swipes that it claims.
/// </summary>
public interface IEdgeSwipeHandler
{
    /// <summary>
    /// Finds the area, usually an output, that contains a new touch down.
    /// </summary>
    /// <param name="layoutX">The horizontal touch position in layout coordinates.</param>
    /// <param name="layoutY">The vertical touch position in layout coordinates.</param>
    /// <param name="area">The area that the edges are measured against.</param>
    /// <returns><see langword="false"/> when no edge swipe can start at this point.</returns>
    bool TryArea(double layoutX, double layoutY, out EdgeSwipeArea area);

    /// <summary>
    /// Called once when the swipe travels far enough inward to take the contact.
    /// </summary>
    /// <param name="recognizer">The recognizer, which gives the edge and the progress.</param>
    void Claimed(EdgeSwipeRecognizer recognizer);

    /// <summary>
    /// Called for each motion of a claimed swipe.
    /// </summary>
    /// <param name="recognizer">The recognizer, which gives the new progress and velocity.</param>
    void Track(EdgeSwipeRecognizer recognizer);

    /// <summary>
    /// Called once when the claimed contact lifts. It is not called when the router cancels the gesture.
    /// </summary>
    /// <param name="recognizer">The recognizer, which gives the outcome and the zone.</param>
    void Finished(EdgeSwipeRecognizer recognizer);
}
