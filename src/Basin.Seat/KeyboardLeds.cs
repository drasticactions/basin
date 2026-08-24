namespace Basin.Seat;

[Flags]
public enum KeyboardLeds
{
    None = 0,
    NumLock = 1 << 0,
    CapsLock = 1 << 1,
    ScrollLock = 1 << 2,
    Compose = 1 << 3,
    Kana = 1 << 4,
}
