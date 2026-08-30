using Basin.Config;
using SkiaSharp;
using Tomlyn.Model;

namespace Dinghy;

internal static class Theme
{
    public static int BorderWidth { get; private set; }

    public static float FontSize { get; private set; }

    public static int TitlebarHeight { get; private set; }

    public static uint BorderActiveOuter { get; private set; }

    public static uint BorderActiveMid { get; private set; }

    public static uint BorderActiveInner { get; private set; }

    public static uint BorderInactiveOuter { get; private set; }

    public static uint BorderInactiveMid { get; private set; }

    public static uint BorderInactiveInner { get; private set; }

    public static uint TitlebarTextActive { get; private set; }

    public static uint TitlebarTextInactive { get; private set; }

    public static uint TitlebarBgActive { get; private set; }

    public static uint TitlebarBgInactive { get; private set; }

    public static uint ButtonBg { get; private set; }

    public static uint ButtonBgPressedClose { get; private set; }

    public static uint ButtonHighlight { get; private set; }

    public static uint ButtonShadow { get; private set; }

    public static bool ShadowsEnabled { get; private set; }

    public static int ShadowsActiveSize { get; private set; }

    public static int ShadowsInactiveSize { get; private set; }

    public static uint ShadowsColor { get; private set; }

    public static uint DesktopBackground { get; private set; }

    public static bool IconsEnabled { get; private set; }

    public static uint MenuBg { get; private set; }

    public static uint MenuText { get; private set; }

    public static uint MenuHighlightBg { get; private set; }

    public static uint MenuHighlightText { get; private set; }

    static Theme() => Reset();

    public static void Reset()
    {
        BorderWidth = 4;
        FontSize = 12f;
        BorderActiveOuter = 0x000000FF;
        BorderActiveMid = 0xC0C0C0FF;
        BorderActiveInner = 0x000000FF;
        BorderInactiveOuter = 0x000000FF;
        BorderInactiveMid = 0xC0C0C0FF;
        BorderInactiveInner = 0x000000FF;
        TitlebarTextActive = 0xFFFFFFFF;
        TitlebarTextInactive = 0x000000FF;
        TitlebarBgActive = 0x000080FF;
        TitlebarBgInactive = 0xFFFFFFFF;
        ButtonBg = 0xC0C0C0FF;
        ButtonBgPressedClose = 0xA0A0A0FF;
        ButtonHighlight = 0xFFFFFFFF;
        ButtonShadow = 0x808080FF;
        ShadowsEnabled = true;
        ShadowsActiveSize = 20;
        ShadowsInactiveSize = 10;
        ShadowsColor = 0x00000033;
        DesktopBackground = 0x008080FF;
        IconsEnabled = true;
        MenuBg = 0xC0C0C0FF;
        MenuText = 0x000000FF;
        MenuHighlightBg = 0x000080FF;
        MenuHighlightText = 0xFFFFFFFF;
        RecomputeTitlebarHeight();
    }

    public static void Apply(TomlTable ui)
    {
        BorderWidth = Int(ui, "border_width") ?? BorderWidth;
        FontSize = Float(ui, "font_size") ?? FontSize;
        (BorderActiveOuter, BorderActiveMid, BorderActiveInner) =
            BorderColors(ui, "border_active", (BorderActiveOuter, BorderActiveMid, BorderActiveInner));
        (BorderInactiveOuter, BorderInactiveMid, BorderInactiveInner) =
            BorderColors(ui, "border_inactive", (BorderInactiveOuter, BorderInactiveMid, BorderInactiveInner));
        TitlebarTextActive = Color(ui, "titlebar_text_active") ?? TitlebarTextActive;
        TitlebarTextInactive = Color(ui, "titlebar_text_inactive") ?? TitlebarTextInactive;
        TitlebarBgActive = Color(ui, "titlebar_bg_active") ?? TitlebarBgActive;
        TitlebarBgInactive = Color(ui, "titlebar_bg_inactive") ?? TitlebarBgInactive;
        ButtonBg = Color(ui, "button_bg") ?? ButtonBg;
        ButtonHighlight = Color(ui, "button_highlight") ?? ButtonHighlight;
        ButtonShadow = Color(ui, "button_shadow") ?? ButtonShadow;
        ShadowsEnabled = ui.TryGetValue("shadows_enabled", out var shadows) && shadows is bool on
            ? on
            : ShadowsEnabled;
        ShadowsActiveSize = Math.Max(Int(ui, "shadows_active_size") ?? ShadowsActiveSize, 0);
        ShadowsInactiveSize = Math.Max(Int(ui, "shadows_inactive_size") ?? ShadowsInactiveSize, 0);
        ShadowsColor = Color(ui, "shadows_color") ?? ShadowsColor;
        DesktopBackground = Color(ui, "desktop_background") ?? DesktopBackground;
        IconsEnabled = ui.TryGetValue("icons_enabled", out var icons) && icons is bool iconsOn
            ? iconsOn
            : IconsEnabled;
        MenuBg = Color(ui, "menu_bg") ?? MenuBg;
        MenuText = Color(ui, "menu_text") ?? MenuText;
        MenuHighlightBg = Color(ui, "menu_highlight_bg") ?? MenuHighlightBg;
        MenuHighlightText = Color(ui, "menu_highlight_text") ?? MenuHighlightText;
        RecomputeTitlebarHeight();
    }

    public static int BorderWidthFor(FrameStyle style) => style == FrameStyle.FixedSize ? 1 : BorderWidth;

    public static SKColor Color(uint rgba) => new(
        (byte)(rgba >> 24),
        (byte)(rgba >> 16),
        (byte)(rgba >> 8),
        (byte)rgba);

    private static void RecomputeTitlebarHeight() =>
        TitlebarHeight = Math.Max(((int)Math.Round(FontSize * 0.75) * 2) + 1, 1);

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

    private static (uint Outer, uint Mid, uint Inner) BorderColors(
        TomlTable table,
        string key,
        (uint Outer, uint Mid, uint Inner) fallback)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlTable colors)
        {
            return fallback;
        }

        return (
            Color(colors, "outer") ?? fallback.Outer,
            Color(colors, "mid") ?? fallback.Mid,
            Color(colors, "inner") ?? fallback.Inner);
    }
}
