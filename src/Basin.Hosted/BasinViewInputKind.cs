namespace Basin.Hosted;

public enum BasinViewInputKind : byte
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
