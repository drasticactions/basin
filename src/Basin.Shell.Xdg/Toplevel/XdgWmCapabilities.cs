using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

/// <summary>
/// The window-management actions that the compositor tells a toplevel it supports. A combination advertises each action in it.
/// </summary>
[Flags]
public enum XdgWmCapabilities
{
    /// <summary>
    /// No action. The client hides the buttons for all of them.
    /// </summary>
    None = 0,
    /// <summary>
    /// The compositor can show a window menu.
    /// </summary>
    WindowMenu = 1,
    /// <summary>
    /// The compositor can maximize the window.
    /// </summary>
    Maximize = 2,
    /// <summary>
    /// The compositor can make the window fullscreen.
    /// </summary>
    Fullscreen = 4,
    /// <summary>
    /// The compositor can minimize the window.
    /// </summary>
    Minimize = 8,
    /// <summary>
    /// Every action.
    /// </summary>
    All = WindowMenu | Maximize | Fullscreen | Minimize,
}
