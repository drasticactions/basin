using Basin.WindowManager;

namespace Dinghy;

internal enum WindowEventKind
{
    Init,
    Close,
    Fullscreen,
    Unfullscreen,
    Maximize,
    Unmaximize,
    Minimize,
}
