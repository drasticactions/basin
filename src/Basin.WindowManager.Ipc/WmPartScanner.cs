using Basin.Capabilities;

namespace Basin.WindowManager.Ipc;

public static class WmPartScanner
{
    public static IReadOnlyList<(FramePart Part, Box Box)> Scan(Rect frame, Func<int, int, FramePart> partAt)
    {
        ArgumentNullException.ThrowIfNull(partAt);
        var extents = new Dictionary<FramePart, (int Left, int Top, int Right, int Bottom)>();
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var part = partAt(x, y);
                if (part == FramePart.None)
                {
                    continue;
                }

                extents[part] = extents.TryGetValue(part, out var e)
                    ? (Math.Min(e.Left, x), Math.Min(e.Top, y), Math.Max(e.Right, x + 1), Math.Max(e.Bottom, y + 1))
                    : (x, y, x + 1, y + 1);
            }
        }

        var parts = new List<(FramePart Part, Box Box)>();
        foreach (var (part, e) in extents.OrderBy(static pair => pair.Key))
        {
            parts.Add((part, new Box(frame.X + e.Left, frame.Y + e.Top, e.Right - e.Left, e.Bottom - e.Top)));
        }

        return parts;
    }

    public static FramePart EdgePart(Edges edges) => edges switch
    {
        Edges.Left => FramePart.Left,
        Edges.Right => FramePart.Right,
        Edges.Top => FramePart.Top,
        Edges.Bottom => FramePart.Bottom,
        Edges.Top | Edges.Left => FramePart.TopLeft,
        Edges.Top | Edges.Right => FramePart.TopRight,
        Edges.Bottom | Edges.Left => FramePart.BottomLeft,
        Edges.Bottom | Edges.Right => FramePart.BottomRight,
        Edges.None => FramePart.None,
        _ => FramePart.Border,
    };
}
