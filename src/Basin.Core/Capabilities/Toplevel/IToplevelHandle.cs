namespace Basin.Capabilities;

/// <summary>
/// A toplevel window that compositor policy can activate, place, resize and close, whatever shell made it.
/// </summary>
public interface IToplevelHandle
{
    /// <summary>
    /// Gets the root surface of the window. It can be <see langword="null"/> when the window has no surface.
    /// </summary>
    Surface? Surface { get; }

    /// <summary>
    /// Gets the title that the client set. It is empty when the client set no title.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Gets the application id, for example the xdg-shell app_id or the X11 class. It is empty when the client set none.
    /// </summary>
    string AppId { get; }

    /// <summary>
    /// Gets the window that this window is transient for, or <see langword="null"/> when it has no parent.
    /// </summary>
    IToplevelHandle? Parent { get; }

    /// <summary>
    /// Gets a value indicating whether the window accepts keyboard focus when policy activates it.
    /// </summary>
    bool WantsFocus { get; }

    /// <summary>
    /// Gets the size in logical pixels that the geometry of the client describes, before policy resizes the window.
    /// </summary>
    (int Width, int Height) NaturalSize { get; }

    /// <summary>
    /// Sets or clears the activated state that the client sees. It does not move keyboard focus.
    /// </summary>
    /// <param name="activated"><see langword="true"/> to show the window as active.</param>
    void SetActivated(bool activated);

    /// <summary>
    /// Sets or clears the maximized state that the client sees.
    /// </summary>
    /// <param name="maximized"><see langword="true"/> to tell the client that it is maximized.</param>
    void SetMaximized(bool maximized);

    /// <summary>
    /// Sets or clears the fullscreen state that the client sees.
    /// </summary>
    /// <param name="fullscreen"><see langword="true"/> to tell the client that it is fullscreen.</param>
    void SetFullscreen(bool fullscreen);

    /// <summary>
    /// Sets or clears the fullscreen state and asks for a position and a size in the same configure. The client sees no size between the old state and the new one.
    /// </summary>
    /// <param name="fullscreen"><see langword="true"/> to tell the client that it is fullscreen.</param>
    /// <param name="x">The left edge in layout coordinates.</param>
    /// <param name="y">The top edge in layout coordinates.</param>
    /// <param name="width">The width in logical pixels.</param>
    /// <param name="height">The height in logical pixels.</param>
    void SetFullscreen(bool fullscreen, int x, int y, int width, int height);

    /// <summary>
    /// Asks the window to take a position and a size. A shell that cannot place its windows, such as xdg-shell, uses only the size.
    /// </summary>
    /// <param name="x">The left edge in layout coordinates.</param>
    /// <param name="y">The top edge in layout coordinates.</param>
    /// <param name="width">The width in logical pixels.</param>
    /// <param name="height">The height in logical pixels.</param>
    void Configure(int x, int y, int width, int height);

    /// <summary>
    /// Asks the client to close the window. The client can ignore the request.
    /// </summary>
    void Close();

    /// <summary>
    /// Finds whether <paramref name="other"/> is an ancestor of this window through the <see cref="Parent"/> chain.
    /// </summary>
    /// <param name="other">The possible ancestor.</param>
    /// <returns><see langword="true"/> when <paramref name="other"/> is a direct or indirect parent.</returns>
    bool IsTransientFor(IToplevelHandle other)
    {
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, other))
            {
                return true;
            }
        }

        return false;
    }
}
