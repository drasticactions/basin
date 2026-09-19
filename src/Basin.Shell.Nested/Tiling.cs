namespace Basin.Shell.Nested;

public static class Tiling
{
    public const int EdgeZone = 16;

    public static TileEdge EdgeAt(Point pointer, Box output, Box workArea, bool tiling, bool topTiling)
    {
        if (!output.Contains(pointer))
            return TileEdge.None;
        if (tiling && pointer.X < workArea.X + EdgeZone)
            return TileEdge.Left;
        if (tiling && pointer.X >= workArea.Right - EdgeZone)
            return TileEdge.Right;
        if (topTiling && pointer.Y <= workArea.Y)
            return TileEdge.Top;
        return TileEdge.None;
    }

    public static Box RectFor(TileEdge edge, Box workArea)
    {
        var half = workArea.Width / 2;
        return edge switch
        {
            TileEdge.Left => workArea with { Width = half },
            TileEdge.Right => workArea with { X = workArea.X + half, Width = workArea.Width - half },
            _ => workArea,
        };
    }
}
