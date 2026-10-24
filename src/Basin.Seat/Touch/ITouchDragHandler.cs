namespace Basin.Seat;

/// <summary>
/// Receives the window move or resize that a <see cref="TouchMoveResize"/> drives from one contact.
/// </summary>
public interface ITouchDragHandler
{
    /// <summary>
    /// Reports a motion of the captured contact.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    void DragTo(uint timeMs, double x, double y);

    /// <summary>
    /// Reports that the drag ended. <see cref="TouchMoveResize.End"/> does not call it.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds, or zero on a cancel.</param>
    /// <param name="cancelled"><see langword="true"/> when the router cancelled the contact instead of a lift.</param>
    void DragEnd(uint timeMs, bool cancelled);
}
