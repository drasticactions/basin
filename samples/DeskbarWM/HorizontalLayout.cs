using Basin.WindowManager;

namespace DeskbarWm;

internal readonly struct HorizontalLayout
{
    public const int MinHeight = 21;
    public const int LeafWidth = 27;
    public const int MaxItemWidth = 175;

    public int Width { get; private init; }

    public int Height { get; private init; }

    public Rect LeafRect { get; private init; }

    public Rect TrayRect { get; private init; }

    public int ListLeft { get; private init; }

    public int ItemWidth { get; private init; }

    public int ItemCount { get; private init; }

    public static HorizontalLayout Compute(
        int width,
        int iconSize,
        int teamCount,
        int naturalItemWidth,
        bool showLabels,
        int trayWidth)
    {
        var height = Math.Max(MinHeight, iconSize + 8);
        var leaf = new Rect(0, 0, LeafWidth, height);
        var tray = new Rect(width - trayWidth, 0, trayWidth, height);
        var listLeft = leaf.Right + 1;
        var available = Math.Max(tray.X - listLeft, 0);
        var natural = showLabels
            ? Math.Min(naturalItemWidth, MaxItemWidth)
            : iconSize + 8;
        var itemWidth = teamCount > 0
            ? Math.Min(natural, Math.Max(available / teamCount, iconSize + 8))
            : natural;
        return new HorizontalLayout
        {
            Width = width,
            Height = height,
            LeafRect = leaf,
            TrayRect = tray,
            ListLeft = listLeft,
            ItemWidth = itemWidth,
            ItemCount = teamCount,
        };
    }

    public Rect ItemRect(int index) => new(ListLeft + (index * ItemWidth), 0, ItemWidth, Height);

    public int ItemAt(int x, int y)
    {
        if (y < 0 || y >= Height || x < ListLeft || ItemWidth <= 0)
        {
            return -1;
        }

        var index = (x - ListLeft) / ItemWidth;
        return index < ItemCount ? index : -1;
    }
}
