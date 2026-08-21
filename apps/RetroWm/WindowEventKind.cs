using Basin.WindowManager;

namespace RetroWm;

internal enum WindowEventKind
{
    Init,
    Close,
    Zoom,
    Unzoom,
    Iconize,
    Fullscreen,
    Unfullscreen,
    Menu,
}
