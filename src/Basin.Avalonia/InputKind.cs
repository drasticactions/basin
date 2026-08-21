using Avalonia.Input;
using Basin.Diagnostics;

namespace Basin.Avalonia;

internal enum InputKind : byte
{
    PointerMotion,
    PointerEnter,
    PointerLeave,
    PointerButton,
    PointerAxis,
    Key,
    TouchDown,
    TouchMotion,
    TouchUp,
    FocusIn,
    FocusOut,
}
