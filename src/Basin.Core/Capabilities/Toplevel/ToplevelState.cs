namespace Basin.Capabilities;

[Flags]
public enum ToplevelState
{
    None = 0,
    Maximized = 1,
    Minimized = 2,
    Activated = 4,
    Fullscreen = 8,
}
