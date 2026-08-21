using Basin.Diagnostics;
using Wayland;
using Wayland.Server;

namespace Basin.Seat;

public enum SerialKind
{
    Other,
    PointerEnter,
    PointerButtonPress,
    PointerButtonRelease,
    KeyboardEnter,
    KeyPress,
    KeyRelease,
    TouchDown,
}
