namespace Basin.Seat;

/// <summary>
/// The modifier keys and lock states of a host keyboard.
/// A value combines one flag for each modifier that is active.
/// </summary>
[Flags]
public enum HostModifiers
{
    /// <summary>
    /// No modifier is active.
    /// </summary>
    None = 0,

    /// <summary>
    /// A Shift key is held.
    /// </summary>
    Shift = 1 << 0,

    /// <summary>
    /// A Control key is held.
    /// </summary>
    Control = 1 << 1,

    /// <summary>
    /// The left Alt key is held.
    /// </summary>
    Alt = 1 << 2,

    /// <summary>
    /// A Meta key is held, for example the Windows key or the Command key.
    /// </summary>
    Meta = 1 << 3,

    /// <summary>
    /// The right Alt key is held. Many layouts use it for the third shift level.
    /// </summary>
    AltGr = 1 << 4,

    /// <summary>
    /// Caps Lock is on.
    /// </summary>
    CapsLock = 1 << 5,

    /// <summary>
    /// Num Lock is on.
    /// </summary>
    NumLock = 1 << 6,
}
