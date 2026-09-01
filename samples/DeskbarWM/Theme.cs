using Basin.Config;
using SkiaSharp;

namespace DeskbarWm;

internal static class Theme
{
    public const float LightenMax = 0.0f;
    public const float Lighten2 = 0.385f;
    public const float Darken1 = 1.147f;
    public const float Darken2 = 1.295f;
    public const float Darken3 = 1.443f;
    public const float DarkenHalf = 1.0735f;
    public const float LightenHalf = 0.1925f;

    public const int BorderResizeLength = 22;
    public const int ResizeKnobSize = 18;

    public static LookFlavor Flavor { get; private set; } = LookFlavor.Haiku;

    public static SKColor FocusTabColor { get; private set; } = new(255, 203, 0);

    public static SKColor FocusTextColor { get; private set; } = new(0, 0, 0);

    public static SKColor FocusFrameColor { get; private set; } = new(224, 224, 224);

    public static SKColor InactiveTabColor { get; private set; } = new(232, 232, 232);

    public static SKColor InactiveTextColor { get; private set; } = new(80, 80, 80);

    public static SKColor InactiveFrameColor { get; private set; } = new(232, 232, 232);

    public static float FontSize { get; private set; } = 12f;

    public static void Reset()
    {
        Flavor = LookFlavor.Haiku;
        FocusTabColor = new SKColor(255, 203, 0);
        FocusTextColor = new SKColor(0, 0, 0);
        FocusFrameColor = new SKColor(224, 224, 224);
        InactiveTabColor = new SKColor(232, 232, 232);
        InactiveTextColor = new SKColor(80, 80, 80);
        InactiveFrameColor = new SKColor(232, 232, 232);
        FontSize = 12f;
    }

    public static void Apply(TomlReader look)
    {
        Flavor = look.Choice("flavor", "haiku", "haiku", "r5") == "r5" ? LookFlavor.R5 : LookFlavor.Haiku;
        if (Flavor == LookFlavor.R5)
        {
            FocusFrameColor = new SKColor(216, 216, 216);
            InactiveFrameColor = new SKColor(216, 216, 216);
        }

        if (TomlColor.Rgba(look.Text("tab-color")) is { } rgba)
        {
            FocusTabColor = new SKColor(
                (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        }

        FontSize = (float)look.Number("font-size", (double)FontSize);
    }

    public static SKColor Tint(SKColor colour, float tint)
    {
        if (tint >= 1f)
        {
            var factor = 2f - tint;
            return new SKColor(
                (byte)Math.Clamp(colour.Red * factor, 0f, 255f),
                (byte)Math.Clamp(colour.Green * factor, 0f, 255f),
                (byte)Math.Clamp(colour.Blue * factor, 0f, 255f),
                colour.Alpha);
        }

        return new SKColor(
            (byte)Math.Clamp(255f - ((255f - colour.Red) * tint), 0f, 255f),
            (byte)Math.Clamp(255f - ((255f - colour.Green) * tint), 0f, 255f),
            (byte)Math.Clamp(255f - ((255f - colour.Blue) * tint), 0f, 255f),
            colour.Alpha);
    }

    public static SKColor TabColor(bool active) => active ? FocusTabColor : InactiveTabColor;

    public static SKColor TextColor(bool active) => active ? FocusTextColor : InactiveTextColor;

    public static SKColor FrameColor(bool active) => active ? FocusFrameColor : InactiveFrameColor;
}
