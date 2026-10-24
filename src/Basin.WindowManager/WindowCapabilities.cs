namespace Basin.WindowManager;

/// <summary>
/// The window actions that the window manager supports, as it tells a window. Combine values to announce more than one action.
/// </summary>
[Flags]
public enum WindowCapabilities
{
    /// <summary>The window manager supports no action.</summary>
    None = 0,

    /// <summary>The window manager can show a window menu.</summary>
    WindowMenu = 1,

    /// <summary>The window manager can maximize the window.</summary>
    Maximize = 2,

    /// <summary>The window manager can make the window fullscreen.</summary>
    Fullscreen = 4,

    /// <summary>The window manager can minimize the window.</summary>
    Minimize = 8,

    /// <summary>The window manager can shade the window to its titlebar.</summary>
    Shade = 16,

    /// <summary>The window manager can keep the window above other windows.</summary>
    Above = 32,

    /// <summary>The window manager can show the window on all workspaces.</summary>
    Stick = 64,

    /// <summary>Every action in this set.</summary>
    All = WindowMenu | Maximize | Fullscreen | Minimize | Shade | Above | Stick,
}
