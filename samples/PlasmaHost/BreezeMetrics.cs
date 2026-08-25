using Basin;
using Basin.Capabilities;

namespace PlasmaHost;

internal sealed class BreezeMetrics
{
    public const int TitleHeight = 30;

    public const int ButtonHit = 24;

    public const int ButtonCircle = 20;

    public const int ButtonGap = 2;

    public const int EdgePad = 4;

    public const int ResizeBand = 4;

    public const int CornerZone = 24;

    public const int CornerRadius = 5;

    public const int MenuWidth = 180;

    public const int MenuItemHeight = 30;

    public const int MenuPadding = 4;

    private BreezeMetrics(FramePart[] left, FramePart[] right, int border)
    {
        LeftButtons = left;
        RightButtons = right;
        BorderWidth = border;
    }

    public IReadOnlyList<FramePart> LeftButtons { get; }

    public IReadOnlyList<FramePart> RightButtons { get; }

    public int BorderWidth { get; }

    public static BreezeMetrics Load()
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
        return new BreezeMetrics(left, right, border);
    }

    public FrameInsets InsetsOf() => new(TitleHeight, BorderWidth, BorderWidth, BorderWidth);

    public BreezeButtons LayoutButtons(int outerWidth, FrameCapabilities capabilities)
    {
        var layout = default(BreezeButtons);
        var y = (TitleHeight - ButtonHit) / 2;
        var left = EdgePad;
        foreach (var part in LeftButtons)
        {
            if (!Wanted(part, capabilities) || left + ButtonHit > outerWidth / 2)
            {
                continue;
            }

            layout = layout.With(part, new Box(left, y, ButtonHit, ButtonHit));
            left += ButtonHit + ButtonGap;
        }

        var right = outerWidth - EdgePad;
        for (var i = RightButtons.Count - 1; i >= 0; i--)
        {
            var part = RightButtons[i];
            if (!Wanted(part, capabilities) || right - ButtonHit < outerWidth / 2)
            {
                continue;
            }

            right -= ButtonHit;
            layout = layout.With(part, new Box(right, y, ButtonHit, ButtonHit));
            right -= ButtonGap;
        }

        return layout with { TitleLeft = left + EdgePad, TitleRight = right + ButtonGap - EdgePad };
    }

    public int MenuItemCount(FrameCapabilities capabilities) =>
        1 + (capabilities.HasFlag(FrameCapabilities.Minimize) ? 1 : 0)
          + (capabilities.HasFlag(FrameCapabilities.Maximize) ? 1 : 0);

    public int MenuHeight(FrameCapabilities capabilities) =>
        MenuItemCount(capabilities) * MenuItemHeight + (2 * MenuPadding);

    private static bool Wanted(FramePart part, FrameCapabilities capabilities) => part switch
    {
        FramePart.Minimize => capabilities.HasFlag(FrameCapabilities.Minimize),
        FramePart.Maximize => capabilities.HasFlag(FrameCapabilities.Maximize),
        _ => true,
    };

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
