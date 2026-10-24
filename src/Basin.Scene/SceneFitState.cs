namespace Basin.Scene;

/// <summary>
/// The window state that decides whether a size mismatch needs a fit.
/// </summary>
public enum SceneFitState
{
    /// <summary>
    /// The window chooses its own size. It never needs a fit.
    /// </summary>
    Floating,
    /// <summary>
    /// The window must fill the maximized area.
    /// </summary>
    Maximized,
    /// <summary>
    /// The window must fill its tile.
    /// </summary>
    Tiled,
    /// <summary>
    /// The window must fill the output.
    /// </summary>
    Fullscreen,
}
