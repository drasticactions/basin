namespace Basin.SoftKeyboard.Avalonia;

/// <summary>
/// Holds the state flags of one key in a <see cref="SoftKeyDraw"/>. A key can carry any combination of flags.
/// </summary>
[Flags]
public enum SoftKeyState
{
    /// <summary>
    /// No flag is set.
    /// </summary>
    None = 0,

    /// <summary>
    /// A touch holds the key down.
    /// </summary>
    Pressed = 1,

    /// <summary>
    /// The shown cap is a function, not text.
    /// </summary>
    Function = 2,

    /// <summary>
    /// Shift applies to the next key only, from a Shift tap or from auto-capitalization.
    /// </summary>
    ShiftOnce = 4,

    /// <summary>
    /// Shift is locked.
    /// </summary>
    ShiftLock = 8,

    /// <summary>
    /// Letters show as capitals.
    /// </summary>
    Capital = 16,

    /// <summary>
    /// The symbol page is active.
    /// </summary>
    Symbol = 32,

    /// <summary>
    /// The key is a Control, Alt or Super key that applies to the next key only.
    /// </summary>
    Latched = 64,

    /// <summary>
    /// The key is a Control, Alt or Super key that is locked.
    /// </summary>
    Locked = 128,
}
