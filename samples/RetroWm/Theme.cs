using Basin.Config;
using SkiaSharp;
using Tomlyn.Model;

namespace RetroWm;

internal static class Theme
{
    public const int DockHeight = 56;
    public const int IconSize = 32;

    public static int BorderWidth { get; private set; }

    public static float FontSize { get; private set; }

    public static int TitlebarHeight { get; private set; }

    public static int SystemBoxWidth { get; private set; }

    public static uint TitleActiveBg { get; set; }

    public static uint TitleActiveText { get; set; }

    public static uint TitleInactiveBg { get; set; }

    public static uint TitleInactiveText { get; set; }

    public static uint WindowLine { get; set; }

    public static uint ChromeBg { get; set; }

    public static uint MenuBg { get; set; }

    public static uint MenuText { get; set; }

    public static uint MenuHighlightBg { get; set; }

    public static uint MenuHighlightText { get; set; }

    public static uint? DesktopBg { get; set; }

    public static uint? DockBg { get; set; }

    public static float DockOpacity { get; set; }

    public static uint DockLabel { get; set; }

    public static uint OutlineColor { get; set; }

    public static uint DropPreview { get; set; }

    public static bool IconDither { get; set; }

    public static bool DockLabels { get; set; }

    static Theme() => Reset();

    public static void Reset()
    {
        BorderWidth = 3;
        FontSize = 12f;
        Fonts.SetConfigured(null);
        TitleActiveBg = Ega.Blue;
        TitleActiveText = Ega.White;
        TitleInactiveBg = Ega.White;
        TitleInactiveText = Ega.Black;
        WindowLine = Ega.Black;
        ChromeBg = Ega.White;
        MenuBg = Ega.White;
        MenuText = Ega.Black;
        MenuHighlightBg = Ega.Black;
        MenuHighlightText = Ega.White;
        DesktopBg = null;
        DockBg = null;
        DockOpacity = 1f;
        DockLabel = Ega.Black;
        OutlineColor = Ega.DarkGray;
        DropPreview = Ega.Blue;
        IconDither = false;
        DockLabels = false;
        RecomputeMetrics();
    }

    public static void Apply(TomlTable ui)
    {
        BorderWidth = Math.Max(Int(ui, "border_width") ?? BorderWidth, 1);
        FontSize = Float(ui, "font_size") ?? FontSize;
        if (ui.TryGetValue("font", out var font) && font is string face)
        {
            Fonts.SetConfigured(face);
        }

        TitleActiveBg = Color(ui, "title_active_bg") ?? TitleActiveBg;
        TitleActiveText = Color(ui, "title_active_text") ?? TitleActiveText;
        TitleInactiveBg = Color(ui, "title_inactive_bg") ?? TitleInactiveBg;
        TitleInactiveText = Color(ui, "title_inactive_text") ?? TitleInactiveText;
        WindowLine = Color(ui, "window_line") ?? WindowLine;
        ChromeBg = Color(ui, "chrome_bg") ?? ChromeBg;
        MenuBg = Color(ui, "menu_bg") ?? MenuBg;
        MenuText = Color(ui, "menu_text") ?? MenuText;
        MenuHighlightBg = Color(ui, "menu_highlight_bg") ?? MenuHighlightBg;
        MenuHighlightText = Color(ui, "menu_highlight_text") ?? MenuHighlightText;
        DesktopBg = Color(ui, "background") ?? DesktopBg;
        DockBg = Color(ui, "dock_bg") ?? DockBg;
        DockOpacity = Math.Clamp(Float(ui, "dock_opacity") ?? DockOpacity, 0f, 1f);
        DockLabel = Color(ui, "dock_label") ?? DockLabel;
        OutlineColor = Color(ui, "outline") ?? OutlineColor;
        DropPreview = Color(ui, "drop_preview") ?? DropPreview;
        if (ui.TryGetValue("icon_dither", out var dither) && dither is bool ditherOn)
        {
            IconDither = ditherOn;
        }

        if (ui.TryGetValue("dock_labels", out var labels) && labels is bool labelsOn)
        {
            DockLabels = labelsOn;
        }

        RecomputeMetrics();
    }

    public static SKColor DockBackground()
    {
        if (DockBg is not { } color)
        {
            return SKColors.Transparent;
        }

        var alpha = (byte)Math.Clamp(
            (int)Math.Round((color & 0xFF) * Math.Clamp(DockOpacity, 0f, 1f)), 0, 255);
        return Color(color).WithAlpha(alpha);
    }

    public static (int Horizontal, int Top, int Bottom) InsetsFor(bool titled) => titled
        ? (BorderWidth, BorderWidth + TitlebarHeight + 1, BorderWidth)
        : (BorderWidth, BorderWidth, BorderWidth);

    public static SKColor Color(uint rgba) => new(
        (byte)(rgba >> 24),
        (byte)(rgba >> 16),
        (byte)(rgba >> 8),
        (byte)rgba);

    private static void RecomputeMetrics()
    {
        TitlebarHeight = Math.Max(((int)Math.Round(FontSize * 0.75) * 2) + 1, 1);
        SystemBoxWidth = Math.Max(TitlebarHeight * 16 / 14, 1);
    }

    private static int? Int(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is long number ? (int)number : null;

    private static float? Float(TomlTable table, string key) => table.TryGetValue(key, out var value)
        ? value switch
        {
            double number => (float)number,
            long number => number,
            _ => null,
        }
        : null;

    private static uint? Color(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) ? TomlColor.Rgba(value) : null;
}
