namespace Basin.Capabilities;

/// <summary>
/// The policy that moves and resizes a toplevel when something other than the client asks for it, for example a control socket.
/// Every toplevel source sends its move and resize requests here.
/// </summary>
public interface IToplevelPlacement
{
    /// <summary>
    /// Moves a toplevel to a position in layout coordinates.
    /// </summary>
    /// <param name="toplevel">The toplevel to move.</param>
    /// <param name="x">The x coordinate of the window geometry.</param>
    /// <param name="y">The y coordinate of the window geometry.</param>
    /// <returns><see langword="true"/> when the policy accepted the move. <see langword="false"/> when it declined it.</returns>
    bool Move(IToplevelHandle toplevel, int x, int y);

    /// <summary>
    /// Resizes a toplevel. The client can still choose a different size.
    /// </summary>
    /// <param name="toplevel">The toplevel to resize.</param>
    /// <param name="width">The width of the window geometry.</param>
    /// <param name="height">The height of the window geometry.</param>
    /// <returns><see langword="true"/> when the policy accepted the resize. <see langword="false"/> when it declined it.</returns>
    bool Resize(IToplevelHandle toplevel, int width, int height);
}
