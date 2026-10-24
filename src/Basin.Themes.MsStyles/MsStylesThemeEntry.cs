namespace Basin.Themes.MsStyles;

/// <summary>
/// Describes one installed visual style that <see cref="MsStylesThemes.Available"/> found and loaded.
/// </summary>
/// <param name="Name">The name of the directory that holds the theme.</param>
/// <param name="Path">The path of the <c>.msstyles</c> file.</param>
/// <param name="Colors">The color scheme names of the theme, in file order.</param>
/// <param name="Sizes">The size names of the theme, in file order.</param>
public sealed record MsStylesThemeEntry(string Name, string Path, IReadOnlyList<string> Colors, IReadOnlyList<string> Sizes);
