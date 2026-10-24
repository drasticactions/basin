namespace Basin.Avalonia;

/// <summary>
/// Tells <see cref="ToplevelWindows"/> how to show a toplevel in a host window.
/// </summary>
public enum ScreenSurfaceKind
{
    /// <summary>An ordinary application window. Its decoration mode and size limits come from the client.</summary>
    Application,
    /// <summary>
    /// A toplevel that stands for a whole screen, for example a nested desktop. It gets a server-side frame, no size limits and an output of its own size.
    /// </summary>
    Screen,
}
