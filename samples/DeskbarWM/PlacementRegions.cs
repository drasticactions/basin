using Basin.WindowManager;

namespace DeskbarWm;

internal static class PlacementRegions
{
    public const float VerticalMiniMultiplier = 2.9f;

    public static DeskbarPlacement? PlacementAt(Rect screen, Point p, int menuBarHeight)
    {
        var barHeight = menuBarHeight;
        var miniDepth = (int)MathF.Floor(barHeight * VerticalMiniMultiplier);
        var divider = screen.Width / 4;
        var half = screen.Height / 2;

        if (Contains(p, screen.X, screen.Y + barHeight, divider, miniDepth - barHeight))
        {
            return Placement(BarOrientation.Vertical, BarSide.Left, BarEnd.Top, DeskbarState.Mini);
        }

        if (Contains(p, screen.Right - divider, screen.Y + barHeight, divider, miniDepth - barHeight))
        {
            return Placement(BarOrientation.Vertical, BarSide.Right, BarEnd.Top, DeskbarState.Mini);
        }

        if (Contains(p, screen.X, screen.Bottom - miniDepth, divider, miniDepth - barHeight))
        {
            return Placement(BarOrientation.Vertical, BarSide.Left, BarEnd.Bottom, DeskbarState.Mini);
        }

        if (Contains(p, screen.Right - divider, screen.Bottom - miniDepth, divider, miniDepth - barHeight))
        {
            return Placement(BarOrientation.Vertical, BarSide.Right, BarEnd.Bottom, DeskbarState.Mini);
        }

        if (Contains(p, screen.X, screen.Y, divider, barHeight))
        {
            return Placement(BarOrientation.Horizontal, BarSide.Left, BarEnd.Top, DeskbarState.Mini);
        }

        if (Contains(p, screen.Right - divider, screen.Y, divider, barHeight))
        {
            return Placement(BarOrientation.Horizontal, BarSide.Right, BarEnd.Top, DeskbarState.Mini);
        }

        if (Contains(p, screen.X, screen.Bottom - barHeight, divider, barHeight))
        {
            return Placement(BarOrientation.Horizontal, BarSide.Left, BarEnd.Bottom, DeskbarState.Mini);
        }

        if (Contains(p, screen.Right - divider, screen.Bottom - barHeight, divider, barHeight))
        {
            return Placement(BarOrientation.Horizontal, BarSide.Right, BarEnd.Bottom, DeskbarState.Mini);
        }

        if (Contains(p, screen.X, screen.Y, divider, screen.Height))
        {
            return Placement(BarOrientation.Vertical, BarSide.Left, BarEnd.Top, DeskbarState.Expando);
        }

        if (Contains(p, screen.Right - divider, screen.Y, divider, screen.Height))
        {
            return Placement(BarOrientation.Vertical, BarSide.Right, BarEnd.Top, DeskbarState.Expando);
        }

        if (Contains(p, screen.X + divider, screen.Y, screen.Width - (2 * divider), half))
        {
            return Placement(BarOrientation.Horizontal, BarSide.Left, BarEnd.Top, DeskbarState.Expando);
        }

        if (Contains(p, screen.X + divider, screen.Bottom - half, screen.Width - (2 * divider), half))
        {
            return Placement(BarOrientation.Horizontal, BarSide.Left, BarEnd.Bottom, DeskbarState.Expando);
        }

        return null;
    }

    private static bool Contains(Point p, int x, int y, int width, int height) =>
        width > 0 && height > 0 && p.X >= x && p.X < x + width && p.Y >= y && p.Y < y + height;

    private static DeskbarPlacement Placement(BarOrientation orientation, BarSide side, BarEnd end, DeskbarState state) =>
        new(orientation, side, end, state);
}
