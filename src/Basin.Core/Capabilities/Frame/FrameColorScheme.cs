namespace Basin.Capabilities;

/// <summary>
/// The color scheme that a window asks its frame to use.
/// </summary>
public enum FrameColorScheme
{
    /// <summary>
    /// The window states no preference, so the theme uses its own default.
    /// </summary>
    Default,
    /// <summary>
    /// The window asks for a light frame.
    /// </summary>
    Light,
    /// <summary>
    /// The window asks for a dark frame.
    /// </summary>
    Dark,
}
