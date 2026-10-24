namespace Basin.Capabilities;

/// <summary>
/// The window operations that a frame can offer. A renderer draws a button or a menu item only for a set flag, and
/// a combined value offers each named operation.
/// </summary>
[Flags]
public enum FrameCapabilities
{
    /// <summary>
    /// No optional operation.
    /// </summary>
    None = 0,
    /// <summary>
    /// The window has a window menu.
    /// </summary>
    WindowMenu = 1,
    /// <summary>
    /// The window can be maximized.
    /// </summary>
    Maximize = 2,
    /// <summary>
    /// The window can be made fullscreen.
    /// </summary>
    Fullscreen = 4,
    /// <summary>
    /// The window can be minimized.
    /// </summary>
    Minimize = 8,
    /// <summary>
    /// The window can be shaded to its title bar.
    /// </summary>
    Shade = 16,
    /// <summary>
    /// The window can be kept above others.
    /// </summary>
    Above = 32,
    /// <summary>
    /// The window can be shown on all workspaces.
    /// </summary>
    Stick = 64,
    /// <summary>
    /// The window is an X11 window that can move between X server densities.
    /// </summary>
    X11Density = 128,
}
