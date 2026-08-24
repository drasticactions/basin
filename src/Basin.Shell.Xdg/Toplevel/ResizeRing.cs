namespace Basin.Shell.Xdg;

public static class ResizeRing
{
    public static ResizeEdges EdgesAt(Box frame, double x, double y, int margin, int corner)
    {
        if (frame.IsEmpty ||
            x < frame.X - margin || x > frame.Right + margin ||
            y < frame.Y || y > frame.Bottom + margin)
        {
            return ResizeEdges.None;
        }

        var edges = ResizeEdges.None;
        if (x < frame.X)
        {
            edges |= ResizeEdges.Left;
        }
        else if (x > frame.Right)
        {
            edges |= ResizeEdges.Right;
        }

        if (y > frame.Bottom)
        {
            edges |= ResizeEdges.Bottom;
            if (edges == ResizeEdges.Bottom)
            {
                if (x < frame.X + corner)
                {
                    edges |= ResizeEdges.Left;
                }
                else if (x > frame.Right - corner)
                {
                    edges |= ResizeEdges.Right;
                }
            }
        }
        else if (edges != ResizeEdges.None && y > frame.Bottom - corner)
        {
            edges |= ResizeEdges.Bottom;
        }

        return edges;
    }

    public static string CursorFor(ResizeEdges edges) => edges switch
    {
        ResizeEdges.Left => "left_side",
        ResizeEdges.Right => "right_side",
        ResizeEdges.Bottom => "bottom_side",
        ResizeEdges.BottomLeft => "bottom_left_corner",
        ResizeEdges.BottomRight => "bottom_right_corner",
        _ => "left_ptr",
    };
}
