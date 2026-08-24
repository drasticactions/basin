using Basin;
using Basin.Effects;

namespace PlasmaHost;

internal static class BreezeShadow
{
    public static DropShadowOptions? Load(double cornerRadius)
    {
        var path = KdeIni.ConfigPath("breezerc");
        const string group = "Common";
        var size = KdeIni.ReadEntry(path, group, "ShadowSize") ?? "ShadowLarge";
        var (offset, primary, secondary) = size switch
        {
            "ShadowNone" => (0.0, default, default(DropShadowLayer)),
            "ShadowSmall" => (4.0, new DropShadowLayer(0, 0, 16, 1), new DropShadowLayer(0, -2, 8, 0.4)),
            "ShadowMedium" => (8.0, new DropShadowLayer(0, 0, 32, 0.9), new DropShadowLayer(0, -4, 16, 0.3)),
            "ShadowVeryLarge" => (16.0, new DropShadowLayer(0, 0, 64, 0.7), new DropShadowLayer(0, -8, 32, 0.1)),
            _ => (12.0, new DropShadowLayer(0, 0, 48, 0.8), new DropShadowLayer(0, -6, 24, 0.2)),
        };

        if (primary.Opacity <= 0 && secondary.Opacity <= 0)
        {
            return null;
        }

        var strength = int.TryParse(KdeIni.ReadEntry(path, group, "ShadowStrength"), out var configured)
            ? Math.Clamp(configured, 25, 255) / 255.0
            : 1.0;

        return new DropShadowOptions
        {
            Primary = primary,
            Secondary = secondary,
            OffsetY = offset,
            CornerRadius = cornerRadius,
            Strength = strength,
            Color = ParseColor(KdeIni.ReadEntry(path, group, "ShadowColor")),
        };
    }

    private static RenderColor ParseColor(string? entry)
    {
        if (string.IsNullOrEmpty(entry))
        {
            return RenderColor.Black;
        }

        var parts = entry.Split(',');
        if (parts.Length < 3 ||
            !byte.TryParse(parts[0].Trim(), out var red) ||
            !byte.TryParse(parts[1].Trim(), out var green) ||
            !byte.TryParse(parts[2].Trim(), out var blue))
        {
            return RenderColor.Black;
        }

        return new RenderColor(red / 255f, green / 255f, blue / 255f, 1);
    }
}
