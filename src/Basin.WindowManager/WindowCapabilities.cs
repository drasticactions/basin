namespace Basin.WindowManager;

[Flags]
public enum WindowCapabilities
{
    None = 0,

    WindowMenu = 1,

    Maximize = 2,

    Fullscreen = 4,

    Minimize = 8,

    All = WindowMenu | Maximize | Fullscreen | Minimize,
}
