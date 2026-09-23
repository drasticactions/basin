using Pixman;

namespace Basin;

public static class RoundedRegion
{
    public static bool Fill(PixmanRegion32 into, int width, int height, int radius,
                            RoundedCorners corners = RoundedCorners.All)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var rows = (int)Math.Round(Math.Min(radius, Math.Min(width, height) / 2.0));
        return Build(into, width, height, rows, radius: rows, [], corners);
    }

    public static bool Fill(PixmanRegion32 into, int width, int height,
                            ReadOnlySpan<int> rowInsets, RoundedCorners corners = RoundedCorners.All)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        return Build(into, width, height, rowInsets.Length, radius: 0, rowInsets, corners);
    }

    public static bool Fill(PixmanRegion32 into, int width, int height,
                            ReadOnlySpan<int> topLeft, ReadOnlySpan<int> topRight,
                            ReadOnlySpan<int> bottomLeft, ReadOnlySpan<int> bottomRight)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var topRows = Math.Max(topLeft.Length, topRight.Length);
        var bottomRows = Math.Max(bottomLeft.Length, bottomRight.Length);
        for (var y = 0; y < topRows; y++)
        {
            AddRow(into, width, height, y, At(topLeft, y), At(topRight, y));
        }

        for (var y = 0; y < bottomRows; y++)
        {
            AddRow(into, width, height, height - 1 - y, At(bottomLeft, y), At(bottomRight, y));
        }

        var straight = height - topRows - bottomRows;
        if (straight > 0)
        {
            into.UnionRect(into, 0, topRows, (uint)width, (uint)straight);
        }

        return !into.IsEmpty;
    }

    public static int CircleInset(int radius, int row)
    {
        var dy = radius - row - 0.5;
        return (int)Math.Round(radius - Math.Sqrt((radius * radius) - (dy * dy)));
    }

    public static int MarcoInset(int corner, int row)
    {
        var r = Math.Sqrt(corner) + corner;
        var dy = r - (row + 0.5);
        return (int)Math.Floor(0.5 + r - Math.Sqrt((r * r) - (dy * dy)));
    }

    private static bool Build(PixmanRegion32 into, int width, int height, int rows, int radius,
                              ReadOnlySpan<int> insets, RoundedCorners corners)
    {
        if (rows <= 0 || corners == RoundedCorners.None)
        {
            into.UnionRect(into, 0, 0, (uint)width, (uint)height);
            return true;
        }

        var top = (corners & RoundedCorners.Top) != 0;
        var bottom = (corners & RoundedCorners.Bottom) != 0;
        var topRows = top ? rows : 0;
        var bottomRows = bottom ? rows : 0;
        for (var y = 0; y < rows; y++)
        {
            var inset = insets.IsEmpty ? CircleInset(radius, y) : insets[y];
            if (top)
            {
                AddRow(into, width, height, y,
                       (corners & RoundedCorners.TopLeft) != 0 ? inset : 0,
                       (corners & RoundedCorners.TopRight) != 0 ? inset : 0);
            }

            if (bottom)
            {
                AddRow(into, width, height, height - 1 - y,
                       (corners & RoundedCorners.BottomLeft) != 0 ? inset : 0,
                       (corners & RoundedCorners.BottomRight) != 0 ? inset : 0);
            }
        }

        var straight = height - topRows - bottomRows;
        if (straight > 0)
        {
            into.UnionRect(into, 0, topRows, (uint)width, (uint)straight);
        }

        return !into.IsEmpty;
    }

    private static int At(ReadOnlySpan<int> insets, int row) => row < insets.Length ? insets[row] : 0;

    private static void AddRow(PixmanRegion32 into, int width, int height, int y, int left, int right)
    {
        var run = width - left - right;
        if (run > 0 && y >= 0 && y < height)
        {
            into.UnionRect(into, Math.Max(0, left), y, (uint)run, 1);
        }
    }
}
