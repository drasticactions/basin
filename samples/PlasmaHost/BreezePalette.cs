using Avalonia.Media;

namespace PlasmaHost;

internal readonly record struct BreezePalette(
    Color ActiveBackground,
    Color ActiveForeground,
    Color InactiveBackground,
    Color InactiveForeground,
    Color Negative,
    Color Focus,
    Color WindowBackground,
    Color WindowForeground,
    Color WindowForegroundInactive,
    Color Highlight)
{
    private const string HeaderGroup = "Colors:Header";
    private const string HeaderInactiveGroup = "Colors:Header][Inactive";
    private const string WindowGroup = "Colors:Window";
    private const string SelectionGroup = "Colors:Selection";
    private const string LegacyGroup = "WM";

    public static BreezePalette Fallback { get; } = new(
        Color.FromRgb(222, 224, 226),
        Color.FromRgb(35, 38, 41),
        Color.FromRgb(239, 240, 241),
        Color.FromRgb(35, 38, 41),
        Color.FromRgb(218, 68, 83),
        Color.FromRgb(61, 174, 233),
        Color.FromRgb(239, 240, 241),
        Color.FromRgb(35, 38, 41),
        Color.FromRgb(112, 125, 138),
        Color.FromRgb(61, 174, 233));

    public bool IsDark =>
        Math.Max(Math.Max(WindowBackground.R, WindowBackground.G), WindowBackground.B) <= 127;

    public static BreezePalette Load(string? palette)
    {
        var fallback = Fallback;
        var path = ResolvePath(palette) ?? KdeIni.ConfigPath("kdeglobals");
        var window = ReadColor(path, WindowGroup, "BackgroundNormal") ?? fallback.WindowBackground;
        var windowForeground = ReadColor(path, WindowGroup, "ForegroundNormal") ?? fallback.WindowForeground;
        var windowInactive =
            ReadColor(path, WindowGroup, "ForegroundInactive") ?? fallback.WindowForegroundInactive;
        var highlight = ReadColor(path, SelectionGroup, "BackgroundNormal") ?? fallback.Highlight;

        Color activeBackground;
        Color activeForeground;
        Color inactiveBackground;
        Color inactiveForeground;
        Color negative;
        Color focus;
        if (KdeIni.GroupExists(path, HeaderGroup))
        {
            activeBackground = ReadColor(path, HeaderGroup, "BackgroundNormal") ?? fallback.ActiveBackground;
            activeForeground = ReadColor(path, HeaderGroup, "ForegroundNormal") ?? fallback.ActiveForeground;
            inactiveBackground = ReadColor(path, HeaderInactiveGroup, "BackgroundNormal") ?? activeBackground;
            inactiveForeground = ReadColor(path, HeaderInactiveGroup, "ForegroundNormal") ?? activeForeground;
            negative = ReadColor(path, HeaderGroup, "ForegroundNegative") ?? fallback.Negative;
            focus = ReadColor(path, HeaderGroup, "DecorationFocus") ?? fallback.Focus;
        }
        else if (KdeIni.GroupExists(path, LegacyGroup))
        {
            activeBackground = ReadColor(path, LegacyGroup, "activeBackground") ?? highlight;
            activeForeground = ReadColor(path, LegacyGroup, "activeForeground")
                ?? ReadColor(path, SelectionGroup, "ForegroundNormal")
                ?? Colors.White;
            inactiveBackground = ReadColor(path, LegacyGroup, "inactiveBackground") ?? activeBackground;
            inactiveForeground = ReadColor(path, LegacyGroup, "inactiveForeground") ?? Darker(activeForeground);
            negative = ReadColor(path, WindowGroup, "ForegroundNegative") ?? fallback.Negative;
            focus = ReadColor(path, WindowGroup, "DecorationFocus") ?? fallback.Focus;
        }
        else
        {
            activeBackground = window;
            activeForeground = windowForeground;
            inactiveBackground = window;
            inactiveForeground = windowInactive;
            negative = ReadColor(path, WindowGroup, "ForegroundNegative") ?? fallback.Negative;
            focus = ReadColor(path, WindowGroup, "DecorationFocus") ?? fallback.Focus;
        }

        return new BreezePalette(
            activeBackground,
            activeForeground,
            inactiveBackground,
            inactiveForeground,
            negative,
            focus,
            window,
            windowForeground,
            windowInactive,
            highlight);
    }

    private static string? ResolvePath(string? palette)
    {
        if (string.IsNullOrEmpty(palette) || palette == "kdeglobals")
        {
            return null;
        }

        if (Path.IsPathRooted(palette))
        {
            return File.Exists(palette) ? palette : null;
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
            var candidate = Path.Combine(directory, $"{palette}.colors");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Color Darker(Color color) =>
        Color.FromRgb((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2));

    private static Color? ReadColor(string path, string group, string key)
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

        return Color.FromRgb(r, g, b);
    }
}
