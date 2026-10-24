namespace Basin.Seat;

/// <summary>
/// Receives the rest of one contact after <see cref="TouchRouter.Capture"/> takes it from its target.
/// </summary>
public interface ITouchCapture
{
    /// <summary>
    /// Reports a motion of the captured contact.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    void Motion(int id, uint timeMs, double x, double y);

    /// <summary>
    /// Reports that the captured contact lifted. The capture then ends.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    void Up(int id, uint timeMs);

    /// <summary>
    /// Reports that the router cancelled the touch sequence, or that a gesture claimed it. The capture then ends.
    /// </summary>
    void Cancel();
}
