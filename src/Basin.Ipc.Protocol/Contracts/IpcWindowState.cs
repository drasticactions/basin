namespace Basin.Ipc;

/// <summary>
/// The state flags of a window.
/// </summary>
/// <param name="Activated"><see langword="true"/> when the window is active.</param>
/// <param name="Maximized"><see langword="true"/> when the window is maximized.</param>
/// <param name="Minimized"><see langword="true"/> when the window is minimized.</param>
/// <param name="Fullscreen"><see langword="true"/> when the window is fullscreen.</param>
/// <param name="NoBorder"><see langword="true"/> when the window has no server-side decorations.</param>
/// <param name="SkipTaskbar"><see langword="true"/> when a taskbar must not list the window.</param>
public readonly record struct IpcWindowState(
    bool Activated,
    bool Maximized,
    bool Minimized,
    bool Fullscreen,
    bool NoBorder,
    bool SkipTaskbar);
