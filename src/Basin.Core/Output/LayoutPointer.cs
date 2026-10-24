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
    /// Gets a value indicating whether a handler is attached to <see cref="Moved"/>. An injector reads it to tell whether a move reaches anything.
    /// </summary>
    public bool HasMovedHandlers => Moved is not null;

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
    /// Moves the pointer to a normalized position on one output, for example from a tablet or a touchscreen. Without an output in the layout, the position maps over the logical bounds of the whole layout.
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

        var bounds = layout.Bounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        Place(bounds.X + normalizedX * bounds.Width, bounds.Y + normalizedY * bounds.Height);
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
        (x, y) = OnPanel(x, y);
        Remember(x, y);
        if (x != X || y != Y)
        {
            X = x;
            Y = y;
            Moved?.Invoke();
        }
    }

    private (double X, double Y) OnPanel(double x, double y)
    {
        if (layout.OutputAt(x, y) is not { } output || output.Scale <= 0)
        {
            return (x, y);
        }

        var mode = output.CurrentMode;
        var (width, height) = output.Transform.SwapsAxes() ? (mode.Height, mode.Width) : (mode.Width, mode.Height);
        if (width <= 0 || height <= 0)
        {
            return (x, y);
        }

        var box = layout.BoxOf(output);
        return (Math.Min(x, box.X + ((width - 1) / output.Scale)), Math.Min(y, box.Y + ((height - 1) / output.Scale)));
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
}
