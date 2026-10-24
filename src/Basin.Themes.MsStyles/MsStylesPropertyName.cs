namespace Basin.Themes.MsStyles;

/// <summary>
/// Maps the INI name of a theme property to its ID and its value type.
/// </summary>
/// <param name="Name">The property name in upper case, for example <c>BGTYPE</c>.</param>
/// <param name="Primitive">The value type that the property stores.</param>
/// <param name="Id">The property ID.</param>
public readonly record struct MsStylesPropertyName(string Name, ThemePrimitive Primitive, ThemeProperty Id);
