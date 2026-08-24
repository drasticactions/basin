namespace Basin.Capabilities;

[Flags]
public enum ToplevelState
{
    None = 0,
    Maximized = 1,
    Minimized = 2,
    Activated = 4,
    Fullscreen = 8,
    NoBorder = 16,
    CanSetNoBorder = 32,
    ExcludedFromCapture = 64,
    SkipTaskbar = 128,
    SkipSwitcher = 256,
}
