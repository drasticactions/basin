namespace Basin.Themes.MsStyles;

/// <summary>
/// Reads the system colors, sizes and fonts of a theme from its <c>sysmetrics</c> class. Every lookup uses part 0 and state 0 and follows the class lookup chain.
/// </summary>
public sealed class MsStylesSysMetrics
{
    internal MsStylesSysMetrics(MsStylesClass? metrics)
    {
        Class = metrics;
    }

    /// <summary>
    /// Gets the <c>sysmetrics</c> class. It is <see langword="null"/> when the theme has no such class, and then every lookup fails.
    /// </summary>
    public MsStylesClass? Class { get; }

    /// <summary>
    /// Gets a value indicating whether the theme asks for flat menus. It is <see langword="false"/> when the theme does not set it.
    /// </summary>
    public bool FlatMenus => Class is not null && Class.TryGetBool(0, 0, ThemeProperty.FlatMenus, out var flat) && flat;

    /// <summary>
    /// Reads one system color.
    /// </summary>
    /// <param name="color">The system color to read.</param>
    /// <param name="value">The color, or the default value when the theme does not set it.</param>
    /// <returns><see langword="true"/> when the theme sets the color.</returns>
    public bool TryGetColor(SysColor color, out MsStylesColor value)
    {
        value = default;
        return Class is not null && Class.TryGetColor(0, 0, (ThemeProperty)color, out value);
    }

    /// <summary>
    /// Reads one system size.
    /// </summary>
    /// <param name="size">The system size to read.</param>
    /// <param name="value">The size in pixels at 96 DPI, or 0 when the theme does not set it.</param>
    /// <returns><see langword="true"/> when the theme sets the size.</returns>
    public bool TryGetSize(SysSize size, out int value)
    {
        value = 0;
        return Class is not null && Class.TryGetSize(0, 0, (ThemeProperty)size, out value);
    }

    /// <summary>
    /// Reads one system font.
    /// </summary>
    /// <param name="font">The system font to read.</param>
    /// <param name="value">The font, or the default value when the theme does not set it.</param>
    /// <returns><see langword="true"/> when the theme sets the font.</returns>
    public bool TryGetFont(SysFont font, out MsStylesFont value)
    {
        value = default;
        return Class is not null && Class.TryGetFont(0, 0, (ThemeProperty)font, out value);
    }
}
