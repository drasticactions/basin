namespace Basin.Capabilities;

/// <summary>
/// The state flags of a toplevel. A value can combine any of the flags.
/// </summary>
[Flags]
public enum ToplevelState
{
    /// <summary>No state is set.</summary>
    None = 0,
    /// <summary>The toplevel is maximized.</summary>
    Maximized = 1,
    /// <summary>The toplevel is minimized.</summary>
    Minimized = 2,
    /// <summary>The toplevel is active.</summary>
    Activated = 4,
    /// <summary>The toplevel is fullscreen.</summary>
    Fullscreen = 8,
    /// <summary>The toplevel has no server-side decorations.</summary>
    NoBorder = 16,
    /// <summary>A client can ask to remove or restore the server-side decorations.</summary>
    CanSetNoBorder = 32,
    /// <summary>Screen captures do not show the toplevel.</summary>
    ExcludedFromCapture = 64,
    /// <summary>A taskbar must not list the toplevel.</summary>
    SkipTaskbar = 128,
    /// <summary>A window switcher must not list the toplevel.</summary>
    SkipSwitcher = 256,
}
