namespace Basin.Effects;

/// <summary>
/// Selects how the full-screen zoom moves its view with the tracked point. The modes are KWin's.
/// </summary>
public enum ZoomTracking
{
    /// <summary>
    /// Places the view so the tracked point keeps its relative position on the screen.
    /// </summary>
    Proportional = 0,

    /// <summary>
    /// Centers the view on the tracked point, but keeps the view inside the screen.
    /// </summary>
    Centered = 1,

    /// <summary>
    /// Moves the view only when the tracked point comes within four pixels of a screen edge.
    /// </summary>
    Push = 2,

    /// <summary>
    /// Keeps the view still. Zoom steps and focus changes recenter it, and the move methods pan it.
    /// </summary>
    Disabled = 3,

    /// <summary>
    /// Centers the view on the tracked point, also when the view goes past the screen edge.
    /// </summary>
    CenteredStrict = 4,
}
