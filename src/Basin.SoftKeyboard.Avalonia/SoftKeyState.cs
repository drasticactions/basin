namespace Basin.SoftKeyboard.Avalonia;

[Flags]
public enum SoftKeyState
{
    None = 0,

    Pressed = 1,

    Function = 2,

    ShiftOnce = 4,

    ShiftLock = 8,

    Capital = 16,

    Symbol = 32,

    Latched = 64,

    Locked = 128,
}
