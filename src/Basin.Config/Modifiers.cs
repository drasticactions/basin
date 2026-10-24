namespace Basin.Config;

/// <summary>
/// Modifier keys as X11 modifier mask bits. A combination means that all of its modifiers are held.
/// </summary>
[Flags]
public enum Modifiers
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
    Ctrl = 4,

    /// <summary>
    /// The Mod1 modifier, usually Alt.
    /// </summary>
    Mod1 = 8,

    /// <summary>
    /// The Mod3 modifier.
    /// </summary>
    Mod3 = 32,

    /// <summary>
    /// The Mod4 modifier, usually Super.
    /// </summary>
    Mod4 = 64,

    /// <summary>
    /// The Mod5 modifier, usually AltGr.
    /// </summary>
    Mod5 = 128,

    /// <summary>
    /// The Alt modifier. It has the same value as <see cref="Mod1"/>.
    /// </summary>
    Alt = Mod1,

    /// <summary>
    /// The Super modifier. It has the same value as <see cref="Mod4"/>.
    /// </summary>
    Super = Mod4,
}
