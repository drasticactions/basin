namespace Basin.Capabilities;

[Flags]
public enum FrameCapabilities
{
    None = 0,
    WindowMenu = 1,
    Maximize = 2,
    Fullscreen = 4,
    Minimize = 8,
    Shade = 16,
    Above = 32,
    Stick = 64,
}
