using Basin.Capabilities;

namespace Basin.WindowManager.Ipc;

/// <summary>
/// Builds frame-part diagnostics for a manager that can hit-test its frames but does not know their part rectangles.
/// </summary>
public static class WmPartScanner
{
    /// <summary>
    /// Hit-tests every pixel of a frame and gets the bounding box of each part. It costs one call per pixel, so use it off the frame path.
    /// </summary>
    /// <param name="frame">The frame rectangle in the compositor's logical space.</param>
    /// <param name="partAt">The hit test, with frame-local x and y.</param>
    /// <returns>One entry for each part found, in part order, with its box in the compositor's logical space. <see cref="FramePart.None"/> is skipped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partAt"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Maps resize edges to the matching frame part.
    /// </summary>
    /// <param name="edges">The edges.</param>
    /// <returns>The side or corner part, <see cref="FramePart.None"/> for no edge, or <see cref="FramePart.Border"/> for any other combination.</returns>
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
