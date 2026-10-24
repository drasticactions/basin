namespace Basin.Capabilities;

/// <summary>
/// The color scheme that the user prefers. The values are the ones of the Settings portal's <c>color-scheme</c> key.
/// </summary>
public enum ColorSchemePreference
{
    /// <summary>
    /// No preference.
    /// </summary>
    None = 0,

    /// <summary>
    /// A dark scheme.
    /// </summary>
    Dark = 1,

    /// <summary>
    /// A light scheme.
    /// </summary>
    Light = 2,
}
