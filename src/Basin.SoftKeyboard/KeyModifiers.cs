namespace Basin.SoftKeyboard;

/// <summary>
/// The modifiers that a soft key carries. A combination of flags applies all of the modifiers at the same time.
/// </summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>
    /// No modifier.
    /// </summary>
    None = 0,
    /// <summary>
    /// The Shift modifier.
    /// </summary>
    Shift = 1,
    /// <summary>
    /// The Control modifier.
    /// </summary>
    Control = 2,
    /// <summary>
    /// The Alt modifier, xkb's Mod1.
    /// </summary>
    Alt = 4,
    /// <summary>
    /// The Super modifier, xkb's Mod4.
    /// </summary>
    Super = 8,
}
