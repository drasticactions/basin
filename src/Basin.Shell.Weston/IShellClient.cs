namespace Basin.Shell.Weston;

/// <summary>
/// The events that the compositor sends to the bound weston shell client. Each call does nothing when no shell client is bound.
/// </summary>
public interface IShellClient
{
    /// <summary>
    /// Asks the shell client to size one of its surfaces, for example a background or a panel.
    /// </summary>
    /// <param name="surface">The shell client's surface to size.</param>
    /// <param name="edges">The resize edges as weston encodes them. A background or a panel uses 0.</param>
    /// <param name="width">The requested width in logical pixels.</param>
    /// <param name="height">The requested height in logical pixels.</param>
    void Configure(Surface surface, uint edges, int width, int height);

    /// <summary>
    /// Asks the shell client to create a lock surface and to set it with <see cref="IShellRoles.SetLockSurface"/>. The client must then call <see cref="IShellRoles.Unlock"/> to resume the desktop.
    /// </summary>
    void PrepareLockSurface();

    /// <summary>
    /// Tells the shell client which cursor to show on its grab surface. The compositor sends it just before the fake pointer enter on that surface.
    /// </summary>
    /// <param name="cursor">The cursor for the grab.</param>
    void GrabCursor(ShellGrabCursor cursor);
}
