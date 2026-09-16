namespace Basin.WindowManager;

[Flags]
public enum WindowCapabilities
{
    None = 0,

    WindowMenu = 1,

    Maximize = 2,

    Fullscreen = 4,

    Minimize = 8,

    Shade = 16,

    Above = 32,

    Stick = 64,

    All = WindowMenu | Maximize | Fullscreen | Minimize | Shade | Above | Stick,
}
