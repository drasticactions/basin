namespace Basin.Frames.MsStyles;

/// <summary>
/// Metric-compatible substitutes for the Windows fonts that themes name.
/// </summary>
public static class MsStylesFontAliases
{
    private static readonly (string Family, string[] Aliases)[] Table =
    [
        ("Segoe UI", ["Selawik", "Open Sans", "Noto Sans"]),
        ("Segoe UI Semibold", ["Selawik Semibold", "Open Sans SemiBold"]),
        ("Tahoma", ["DejaVu Sans", "Verdana", "Liberation Sans"]),
        ("Trebuchet MS", ["Fira Sans", "Ubuntu", "DejaVu Sans"]),
        ("Verdana", ["DejaVu Sans"]),
        ("Franklin Gothic Medium", ["Libre Franklin", "Archivo"]),
        ("Microsoft Sans Serif", ["Liberation Sans", "DejaVu Sans"]),
        ("MS Shell Dlg", ["Liberation Sans", "DejaVu Sans"]),
        ("MS Shell Dlg 2", ["DejaVu Sans", "Liberation Sans"]),
        ("Arial", ["Liberation Sans", "Arimo"]),
        ("Calibri", ["Carlito"]),
        ("Cambria", ["Caladea"]),
    ];

    /// <summary>
    /// Gets the substitutes for a font family, in order of preference. The family name is compared without case.
    /// </summary>
    /// <param name="family">The family name that the theme uses.</param>
    /// <returns>The substitute family names, or an empty list when the table has no entry.</returns>
    public static IReadOnlyList<string> For(string family)
    {
        foreach (var (name, aliases) in Table)
        {
            if (string.Equals(name, family, StringComparison.OrdinalIgnoreCase))
            {
                return aliases;
            }
        }

        return [];
    }
}
