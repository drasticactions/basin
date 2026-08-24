using Basin.Capabilities;

namespace PlasmaHost;

internal sealed class KdeDecorationConfig
{
    private KdeDecorationConfig(FramePart[] left, FramePart[] right, int border)
    {
        LeftButtons = left;
        RightButtons = right;
        BorderWidth = border;
    }

    public IReadOnlyList<FramePart> LeftButtons { get; }

    public IReadOnlyList<FramePart> RightButtons { get; }

    public int BorderWidth { get; }

    public static KdeDecorationConfig Load()
    {
        var path = KdeIni.ConfigPath("kwinrc");
        const string group = "org.kde.kdecoration2";
        var left = ParseButtons(KdeIni.ReadEntry(path, group, "ButtonsOnLeft") ?? "MSE");
        var right = ParseButtons(KdeIni.ReadEntry(path, group, "ButtonsOnRight") ?? "HIAX");
        var border = KdeIni.ReadEntry(path, group, "BorderSize") switch
        {
            "Tiny" => 1,
            "Normal" => 4,
            "Large" => 8,
            "VeryLarge" => 12,
            "Huge" => 16,
            "VeryHuge" => 24,
            "Oversized" => 40,
            _ => 0,
        };
        return new KdeDecorationConfig(left, right, border);
    }

    private static FramePart[] ParseButtons(string letters)
    {
        var parts = new List<FramePart>(letters.Length);
        foreach (var letter in letters)
        {
            var part = letter switch
            {
                'M' => FramePart.Menu,
                'I' => FramePart.Minimize,
                'A' => FramePart.Maximize,
                'X' => FramePart.Close,
                _ => FramePart.None,
            };
            if (part != FramePart.None)
            {
                parts.Add(part);
            }
        }

        return [.. parts];
    }
}
