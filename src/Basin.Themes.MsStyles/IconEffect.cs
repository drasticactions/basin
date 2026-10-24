namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects the effect that a theme applies to an icon. The values are the uxtheme <c>ICONEFFECT</c> values.
/// </summary>
public enum IconEffect
{
    /// <summary>
    /// The icon has no effect.
    /// </summary>
    None = 0,
    /// <summary>
    /// The icon has a glow.
    /// </summary>
    Glow = 1,
    /// <summary>
    /// The icon has a shadow.
    /// </summary>
    Shadow = 2,
    /// <summary>
    /// The icon pulses.
    /// </summary>
    Pulse = 3,
    /// <summary>
    /// The icon draws with alpha blending.
    /// </summary>
    Alpha = 4,
}
