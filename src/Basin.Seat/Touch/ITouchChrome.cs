namespace Basin.Seat;

/// <summary>
/// Lets compositor-drawn chrome, such as title bars and panels, take touch contacts before client surfaces do.
/// </summary>
public interface ITouchChrome
{
    /// <summary>
    /// Offers a new contact to the chrome.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns><see langword="true"/> when the chrome takes the contact. The router then sends it all later events of the contact.</returns>
    bool TryPress(int id, uint timeMs, double x, double y);

    /// <summary>
    /// Reports a motion of a contact that the chrome took.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    void Motion(int id, uint timeMs, double x, double y);

    /// <summary>
    /// Reports that a contact that the chrome took lifted.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The last horizontal position in layout coordinates.</param>
    /// <param name="y">The last vertical position in layout coordinates.</param>
    void Release(int id, uint timeMs, double x, double y);

    /// <summary>
    /// Drops every contact that the chrome holds. The router calls it on a touch cancel and when a gesture claims.
    /// </summary>
    void Cancel();
}
