namespace Basin;

/// <summary>
/// Keeps the pointer position in layout coordinates and keeps it inside the layout. It holds position only, not buttons or focus.
/// </summary>
/// <param name="layout">The layout that bounds the pointer.</param>
public sealed class LayoutPointer(OutputLayout layout)
{
    private IOutput? _home;
    private double _homeX;
    private double _homeY;

    /// <summary>
    /// Gets the horizontal position in logical layout coordinates.
    /// </summary>
    public double X { get; private set; }

    /// <summary>
    /// Gets the vertical position in logical layout coordinates.
    /// </summary>
    public double Y { get; private set; }

    /// <summary>
    /// Occurs when <see cref="X"/> or <see cref="Y"/> changes. A move that the layout clamps back to the same point does not raise it.
    /// </summary>
    public event Action? Moved;

    /// <summary>
    /// Puts the pointer back at the same relative place on the output it was on, after the layout or a scale changed. It clamps to the nearest point when that output is gone. Call it after each output reconfiguration.
    /// </summary>
    public void Reposition()
    {
        if (_home is { } home && layout.BoxOf(home) is { Width: > 0, Height: > 0 } box)
        {
            Place(box.X + (_homeX * box.Width), box.Y + (_homeY * box.Height));
            return;
        }

        Place(X, Y);
    }

    /// <summary>
    /// Moves the pointer by a relative amount. The result is clamped to the layout, so the pointer can cross from one output to the next.
    /// </summary>
    /// <param name="dx">The horizontal distance in logical pixels.</param>
    /// <param name="dy">The vertical distance in logical pixels.</param>
    public void Motion(double dx, double dy) => Place(X + dx, Y + dy);

    /// <summary>
    /// Moves the pointer to a normalized position on one output, for example from a tablet or a touchscreen. Without an output in the layout, the position maps over the whole layout.
    /// </summary>
    /// <param name="output">The output that the device maps to, or <see langword="null"/> for the whole layout.</param>
    /// <param name="normalizedX">The horizontal position from 0 to 1.</param>
    /// <param name="normalizedY">The vertical position from 0 to 1.</param>
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

    /// <summary>
    /// Moves the pointer to a layout position. The result is clamped to the layout.
    /// </summary>
    /// <param name="x">The horizontal position in logical layout coordinates.</param>
    /// <param name="y">The vertical position in logical layout coordinates.</param>
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
