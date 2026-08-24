namespace Basin.Shell.Xdg;

public readonly record struct ResizeDrag(ResizeEdges Edges, Box Start, double GrabX, double GrabY)
{
    public Box BoxFor(double x, double y, int currentX, int currentY, int minWidth = 32, int minHeight = 32)
    {
        var dx = (int)(x - GrabX);
        var dy = (int)(y - GrabY);
        var width = Math.Max(
            Start.Width + ((Edges & ResizeEdges.Left) != ResizeEdges.None ? -dx
                : (Edges & ResizeEdges.Right) != ResizeEdges.None ? dx : 0), minWidth);
        var height = Math.Max(
            Start.Height + ((Edges & ResizeEdges.Top) != ResizeEdges.None ? -dy
                : (Edges & ResizeEdges.Bottom) != ResizeEdges.None ? dy : 0), minHeight);
        var moveX = (Edges & ResizeEdges.Left) != ResizeEdges.None ? Start.Right - width : currentX;
        var moveY = (Edges & ResizeEdges.Top) != ResizeEdges.None ? Start.Bottom - height : currentY;
        return new Box(moveX, moveY, width, height);
    }
}
