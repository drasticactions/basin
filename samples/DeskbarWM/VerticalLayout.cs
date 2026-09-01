using Basin.WindowManager;

namespace DeskbarWm;

internal readonly struct VerticalLayout
{
    public const int MenuBarHeight = 21;
    public const int Gutter = 5;

    public int Width { get; private init; }

    public Rect LeafRect { get; private init; }

    public Rect TrayRect { get; private init; }

    public int ListTop { get; private init; }

    public int RowHeight { get; private init; }

    public int ContentHeight { get; private init; }

    public static VerticalLayout Compute(int width, int iconSize, float fontSize, int teamCount, int trayHeight)
    {
        var leaf = new Rect(0, 0, width, MenuBarHeight);
        var tray = new Rect(0, leaf.Bottom, width, trayHeight);
        var listTop = tray.Bottom + (trayHeight > 0 ? 1 : 0);
        var rowHeight = Math.Max(iconSize + 8, (int)MathF.Ceiling(fontSize) + 10);
        var contentHeight = listTop + (teamCount * rowHeight) + Gutter;
        return new VerticalLayout
        {
            Width = width,
            LeafRect = leaf,
            TrayRect = tray,
            ListTop = listTop,
            RowHeight = rowHeight,
            ContentHeight = contentHeight,
        };
    }

    public Rect RowRect(int index) => new(0, ListTop + (index * RowHeight), Width, RowHeight);

    public int RowAt(int x, int y)
    {
        if (x < 0 || x >= Width || y < ListTop)
        {
            return -1;
        }

        return (y - ListTop) / Math.Max(RowHeight, 1);
    }

    public static int DefaultWidth(int iconSize) => 129 + Math.Max(iconSize - 16, 0);
}
