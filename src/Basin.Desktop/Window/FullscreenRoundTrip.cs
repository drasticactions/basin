using Basin.Capabilities;

namespace Basin.Desktop;

/// <summary>
/// Keeps the windowed size out of a float and tile round trip of a fullscreen window, and clears fullscreen with the restored geometry in one configure. One instance serves one window.
/// </summary>
public sealed class FullscreenRoundTrip
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FullscreenRoundTrip"/> class for one window.
    /// </summary>
    /// <param name="window">The window that the configures go to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public FullscreenRoundTrip(IToplevelHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Window = window;
    }

    /// <summary>
    /// Gets the window that the configures go to.
    /// </summary>
    public IToplevelHandle Window { get; }

    /// <summary>
    /// Gets a value indicating whether the window was fullscreen when policy floated it, and the client has not left fullscreen since.
    /// </summary>
    public bool RefullscreenOnTile { get; private set; }

    /// <summary>
    /// Records that policy floats the window. When the window is fullscreen, it clears fullscreen at the current geometry in one configure and remembers to restore fullscreen on the next tile.
    /// </summary>
    /// <param name="fullscreen"><see langword="true"/> when the window is fullscreen now.</param>
    /// <param name="current">The current geometry of the window in layout coordinates.</param>
    /// <returns><see cref="FloatSize.Keep"/> when the consumer must float the window at <paramref name="current"/> without a resize, else <see cref="FloatSize.Policy"/>.</returns>
    public FloatSize Float(bool fullscreen, in Box current)
    {
        if (!fullscreen)
        {
            return FloatSize.Policy;
        }

        Window.SetFullscreen(false, current.X, current.Y, current.Width, current.Height);
        RefullscreenOnTile = true;
        return FloatSize.Keep;
    }

    /// <summary>
    /// Records that policy tiles the window again and clears the record.
    /// </summary>
    /// <param name="fullscreen"><see langword="true"/> when the window is fullscreen now.</param>
    /// <returns><see langword="true"/> when the consumer must make the window fullscreen before it adds the window to the layout.</returns>
    public bool Tile(bool fullscreen)
    {
        var refullscreen = RefullscreenOnTile && !fullscreen;
        RefullscreenOnTile = false;
        return refullscreen;
    }

    /// <summary>
    /// Records that the client left fullscreen by its own request. The next tile then adds the window as a normal window.
    /// </summary>
    public void ClientLeftFullscreen() => RefullscreenOnTile = false;

    /// <summary>
    /// Clears fullscreen and sets the restored geometry in one configure. It also clears the record.
    /// </summary>
    /// <param name="restored">The geometry that the window gets back, in layout coordinates.</param>
    public void ExitFullscreen(in Box restored)
    {
        RefullscreenOnTile = false;
        Window.SetFullscreen(false, restored.X, restored.Y, restored.Width, restored.Height);
    }
}
