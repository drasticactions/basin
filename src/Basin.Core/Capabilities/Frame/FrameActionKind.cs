namespace Basin.Capabilities;

/// <summary>
/// The kinds of <see cref="FrameAction"/>.
/// </summary>
public enum FrameActionKind
{
    /// <summary>
    /// Close the window.
    /// </summary>
    Close,
    /// <summary>
    /// Maximize the window, or restore it when it is maximized.
    /// </summary>
    ToggleMaximize,
    /// <summary>
    /// Minimize the window.
    /// </summary>
    Minimize,
    /// <summary>
    /// Start an interactive move.
    /// </summary>
    Move,
    /// <summary>
    /// Start an interactive resize on the edges that <see cref="FrameAction.Edges"/> names.
    /// </summary>
    Resize,

    /// <summary>
    /// Show the window menu.
    /// </summary>
    ShowMenu,

    /// <summary>
    /// Roll the window up to its title bar, or roll it down.
    /// </summary>
    ToggleShade,
    /// <summary>
    /// Keep the window above others, or stop doing so.
    /// </summary>
    ToggleAbove,
    /// <summary>
    /// Show the window on all workspaces, or on one only.
    /// </summary>
    ToggleSticky,
    /// <summary>
    /// Move an X11 window between the low-density and the full-density X server, through <see cref="IX11DensityControl"/>.
    /// </summary>
    ToggleX11Density,
}
