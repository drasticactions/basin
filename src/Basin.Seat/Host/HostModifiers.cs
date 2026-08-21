namespace Basin.Seat;

[Flags]
public enum HostModifiers
{
    None = 0,

    Shift = 1 << 0,

    Control = 1 << 1,

    Alt = 1 << 2,

    Meta = 1 << 3,

    AltGr = 1 << 4,

    CapsLock = 1 << 5,

    NumLock = 1 << 6,
}
