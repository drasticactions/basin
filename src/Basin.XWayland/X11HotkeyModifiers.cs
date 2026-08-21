using System.Text;
using Basin.Diagnostics;
using Xcb.Native;

namespace Basin.XWayland;

[Flags]
public enum X11HotkeyModifiers
{
    None = 0,

    Shift = 1,

    Ctrl = 2,

    Alt = 4,

    Super = 8,
}
