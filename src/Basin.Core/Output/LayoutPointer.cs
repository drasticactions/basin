namespace Basin;

public sealed class LayoutPointer(OutputLayout layout)
{
    private IOutput? _home;
    private double _homeX;
    private double _homeY;

    public double X { get; private set; }

    public double Y { get; private set; }

    public event Action? Moved;

    public void Reposition()
    {
        if (_home is { } home && layout.BoxOf(home) is { Width: > 0, Height: > 0 } box)
        {
            Place(box.X + (_homeX * box.Width), box.Y + (_homeY * box.Height));
            return;
        }

        Place(X, Y);
    }

    public void Motion(double dx, double dy) => Place(X + dx, Y + dy);

    public void MotionAbsolute(IOutput? output, double normalizedX, double normalizedY)
    {
        if (output is not null && layout.BoxOf(output) is { IsEmpty: false } box)
        {
            Place(box.X + normalizedX * box.Width, box.Y + normalizedY * box.Height);
            return;
        }

        var (minX, minY, maxX, maxY) = Extents();
        Place(minX + normalizedX * (maxX - minX), minY + normalizedY * (maxY - minY));
    }

    public void Warp(double x, double y) => Place(x, y);

    private void Place(double x, double y)
    {
        (x, y) = layout.ClosestPoint(x, y);
        Remember(x, y);
        if (x != X || y != Y)
        {
            X = x;
            Y = y;
            Moved?.Invoke();
        }
    }

    private void Remember(double x, double y)
    {
        if (layout.OutputAt(x, y) is not { } output ||
            layout.BoxOf(output) is not { Width: > 0, Height: > 0 } box)
        {
            return;
        }

        _home = output;
        _homeX = (x - box.X) / box.Width;
        _homeY = (y - box.Y) / box.Height;
    }

    private (double MinX, double MinY, double MaxX, double MaxY) Extents()
    {
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        var first = true;
        foreach (var (output, position) in layout.Outputs)
        {
            var mode = output.CurrentMode;
            if (first)
            {
                (minX, minY, maxX, maxY) = (position.X, position.Y, position.X + mode.Width, position.Y + mode.Height);
                first = false;
            }
            else
            {
                minX = Math.Min(minX, position.X);
                minY = Math.Min(minY, position.Y);
                maxX = Math.Max(maxX, position.X + mode.Width);
                maxY = Math.Max(maxY, position.Y + mode.Height);
            }
        }

        return (minX, minY, maxX, maxY);
    }
}
