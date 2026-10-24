namespace Basin.Shell.Nested;

/// <summary>
/// The modifiers of a shell chord. A combined value means that all of the named modifiers are held.
/// </summary>
[Flags]
public enum ShellModifiers
{
    /// <summary>
    /// No modifier.
    /// </summary>
    None = 0,

    /// <summary>
    /// Either Shift key.
    /// </summary>
    Shift = 1,

    /// <summary>
    /// Either Control key.
    /// </summary>
    Ctrl = 2,

    /// <summary>
    /// Either Alt key, AltGr included.
    /// </summary>
    Alt = 4,

    /// <summary>
    /// Either Super key.
    /// </summary>
    Super = 8,
}
