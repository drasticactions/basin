namespace Basin.Seat;

/// <summary>
/// The keyboard LEDs that the active xkb state lights. Values combine, one bit for each LED.
/// </summary>
[Flags]
public enum KeyboardLeds
{
    /// <summary>No LED is lit.</summary>
    None = 0,
    /// <summary>The Num Lock LED.</summary>
    NumLock = 1 << 0,
    /// <summary>The Caps Lock LED.</summary>
    CapsLock = 1 << 1,
    /// <summary>The Scroll Lock LED.</summary>
    ScrollLock = 1 << 2,
    /// <summary>The Compose LED.</summary>
    Compose = 1 << 3,
    /// <summary>The Kana LED.</summary>
    Kana = 1 << 4,
}
