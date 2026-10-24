namespace Basin.Themes.MsStyles;

/// <summary>
/// Maps one value of an enumerated theme property to the name that a theme INI file uses.
/// </summary>
/// <param name="Property">The property that takes the value.</param>
/// <param name="Value">The integer value that the property stores.</param>
/// <param name="Name">The name in the INI text, for example <c>IMAGEFILE</c>.</param>
public readonly record struct MsStylesEnumName(ThemeProperty Property, int Value, string Name);
