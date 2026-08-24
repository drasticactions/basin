using SkiaSharp;

namespace PlasmaHost;

internal readonly record struct BreezePalette(
    SKColor ActiveBackground,
    SKColor ActiveForeground,
    SKColor InactiveBackground,
    SKColor InactiveForeground,
    SKColor Negative,
    SKColor Focus)
{
    public static BreezePalette Fallback { get; } = new(
        new SKColor(227, 229, 231),
        new SKColor(35, 38, 41),
        new SKColor(239, 240, 241),
        new SKColor(112, 125, 138),
        new SKColor(218, 68, 83),
        new SKColor(61, 174, 233));

    public static BreezePalette Load(string? palette)
    {
        var path = ResolvePath(palette);
        if (path is null)
        {
            return Fallback;
        }

        var fallback = Fallback;
        return new BreezePalette(
            ReadColor(path, "WM", "activeBackground") ?? fallback.ActiveBackground,
            ReadColor(path, "WM", "activeForeground") ?? fallback.ActiveForeground,
            ReadColor(path, "WM", "inactiveBackground") ?? fallback.InactiveBackground,
            ReadColor(path, "WM", "inactiveForeground") ?? fallback.InactiveForeground,
            ReadColor(path, "Colors:Window", "ForegroundNegative") ?? fallback.Negative,
            ReadColor(path, "Colors:Window", "DecorationFocus") ?? fallback.Focus);
    }

    private static string? ResolvePath(string? palette)
    {
        if (palette is { Length: > 0 } && Path.IsPathRooted(palette))
        {
            return File.Exists(palette) ? palette : null;
        }

        var name = palette;
        if (string.IsNullOrEmpty(name))
        {
            name = KdeIni.ReadEntry(KdeIni.ConfigPath("kdeglobals"), "General", "ColorScheme");
        }

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        foreach (var directory in (ReadOnlySpan<string>)
        [
            Path.Combine(dataHome, "color-schemes"),
            "/usr/share/color-schemes",
        ])
        {
            var candidate = Path.Combine(directory, $"{name}.colors");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static SKColor? ReadColor(string path, string group, string key)
    {
        var value = KdeIni.ReadEntry(path, group, key);
        if (value is null)
        {
            return null;
        }

        var parts = value.Split(',');
        if (parts.Length < 3 ||
            !byte.TryParse(parts[0], out var r) ||
            !byte.TryParse(parts[1], out var g) ||
            !byte.TryParse(parts[2], out var b))
        {
            return null;
        }

        return new SKColor(r, g, b);
    }
}
