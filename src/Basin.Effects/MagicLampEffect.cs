using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class MagicLampEffect : IMeshTransform
{
    public const int CellSize = 40;

    private const string NodeName = "magiclamp";

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly MeshGrid _grid = new();
    private EffectTimeline _timeline;
    private Box _window;
    private Box _icon;
    private MinimizeEdge _edge;
    private bool _restoring;
    private double _progress;
    private SceneTransform? _node;

    public bool IsRunning => _node is { IsDestroyed: false };

    public bool IsRestoring => _restoring;

    public static (Box Icon, MinimizeEdge Edge) FallbackTarget(in Box window, double cursorX, double cursorY)
    {
        var x = cursorX;
        var y = cursorY;
        var edge = MinimizeEdge.Top;
        if (x >= window.X && x <= window.Right && y >= window.Y && y <= window.Bottom)
        {
            var left = x - window.X;
            var right = window.Right - x;
            var top = y - window.Y;
            var bottom = window.Bottom - y;
            var nearest = top;
            edge = MinimizeEdge.Top;
            if (left < nearest)
            {
                nearest = left;
                edge = MinimizeEdge.Left;
            }

            if (bottom < nearest)
            {
                nearest = bottom;
                edge = MinimizeEdge.Bottom;
            }

            if (right < nearest)
            {
                edge = MinimizeEdge.Right;
            }

            switch (edge)
            {
                case MinimizeEdge.Top:
                    y = window.Y;
                    break;
                case MinimizeEdge.Left:
                    x = window.X;
                    break;
                case MinimizeEdge.Bottom:
                    y = window.Bottom;
                    break;
                case MinimizeEdge.Right:
                    x = window.Right;
                    break;
            }
        }
        else if (y < window.Y)
        {
            edge = MinimizeEdge.Top;
        }
        else if (x < window.X)
        {
            edge = MinimizeEdge.Left;
        }
        else if (y > window.Bottom)
        {
            edge = MinimizeEdge.Bottom;
        }
        else if (x > window.Right)
        {
            edge = MinimizeEdge.Right;
        }

        return (new Box((int)Math.Round(x), (int)Math.Round(y), 0, 0), edge);
    }

    public bool Begin(
        TransformStack stack,
        in Box window,
        in Box icon,
        MinimizeEdge edge,
        bool restoring,
        in FrameTick now,
        AnimationDuration duration)
    {
        if (!Begin(stack, window, icon, edge, restoring, duration))
        {
            return false;
        }

        _timeline.Anchor(now);
        return true;
    }

    public bool Begin(
        TransformStack stack,
        in Box window,
        in Box icon,
        MinimizeEdge edge,
        bool restoring,
        AnimationDuration duration)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (duration.IsDisabled || window.IsEmpty)
        {
            return false;
        }

        _window = window;
        _icon = icon;
        _edge = edge;
        _restoring = restoring;
        _progress = restoring ? 1 : 0;
        _node = stack.Get(NodeName) ?? stack.Add(TransformStack.ZOrder.Effect, NodeName);
        _timeline.Easing = EasingCurve.Linear;
        _timeline.Start(duration.Nanos);
        _node.Deformer = this;
        _node.NotifyDeformed();
        return true;
    }

    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        if (_node is not { IsDestroyed: false } node)
        {
            return false;
        }

        var raw = _timeline.Progress(tick);
        _progress = _restoring ? 1.0 - raw : raw;
        node.NotifyDeformed();
        if (_timeline.Running(tick))
        {
            return true;
        }

        node.NotifyDeformed();
        return false;
    }

    public void End(TransformStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (_node is { IsDestroyed: false } node)
        {
            node.Deformer = null;
            node.NotifyDeformed();
        }

        stack.Remove(NodeName);
        _node = null;
    }

    public Box MapBounds(in Box childBounds)
    {
        var target = new Box(
            _icon.X - _window.X + childBounds.X,
            _icon.Y - _window.Y + childBounds.Y,
            Math.Max(1, _icon.Width),
            Math.Max(1, _icon.Height));
        var x0 = Math.Min(childBounds.X, target.X);
        var y0 = Math.Min(childBounds.Y, target.Y);
        var x1 = Math.Max(childBounds.Right, target.Right);
        var y1 = Math.Max(childBounds.Bottom, target.Bottom);
        return new Box(x0, y0, x1 - x0, y1 - y0);
    }

    public int VertexCount(in Box childBounds)
    {
        _grid.Layout(childBounds, CellSize);
        return _grid.VertexCount;
    }

    public void WriteVertices(in Box childBounds, Span<MeshVertex> into)
    {
        _grid.Layout(childBounds, CellSize);
        var pointsX = _grid.PointsX;
        var pointsY = _grid.PointsY;
        var progress = _progress;
        for (var row = 0; row <= _grid.Rows; row++)
        {
            for (var column = 0; column <= _grid.Columns; column++)
            {
                var index = _grid.Index(column, row);
                var localX = _grid.SourceX(column) - childBounds.X;
                var localY = _grid.SourceY(row) - childBounds.Y;
                Deform(localX, localY, progress, out var x, out var y);
                pointsX[index] = (float)(childBounds.X + x);
                pointsY[index] = (float)(childBounds.Y + y);
            }
        }

        _grid.Write(into);
    }

    private static double Pull(double offset, double span)
    {
        if (Math.Abs(span) < 1e-6)
        {
            return 1.0;
        }

        return Math.Abs(Math.Min(offset / span, 1.0));
    }

    private void Deform(double x, double y, double progress, out double outX, out double outY)
    {
        double width = _window.Width;
        double height = _window.Height;
        double iconX = _icon.X;
        double iconY = _icon.Y;
        double iconWidth = _icon.Width;
        double iconHeight = _icon.Height;
        double geoX = _window.X;
        double geoY = _window.Y;

        switch (_edge)
        {
            case MinimizeEdge.Bottom:
            {
                var cube = height * height * height;
                var maxY = iconY - geoY;
                var factor = y + ((height - y) * progress);
                var offset = (iconY + y - geoY) * progress * (factor * factor * factor / cube);
                var pull = Pull(offset, iconY - geoY - y);
                outX = ((iconX + (iconWidth * (x / width)) - (x + geoX)) * pull) + x;
                outY = Math.Min(maxY, y + offset);
                return;
            }

            case MinimizeEdge.Top:
            {
                var cube = height * height * height;
                var minY = iconY + iconHeight - geoY;
                var factor = height - y + (y * progress);
                var offset = (geoY - iconHeight + height + y - iconY) * progress * (factor * factor * factor / cube);
                var pull = Pull(offset, geoY - iconHeight + height - iconY - (height - y));
                outX = ((iconX + (iconWidth * (x / width)) - (x + geoX)) * pull) + x;
                outY = Math.Max(minY, y - offset);
                return;
            }

            case MinimizeEdge.Left:
            {
                var cube = width * width * width;
                var minX = iconX + iconWidth - geoX;
                var factor = width - x + (x * progress);
                var offset = (geoX - iconWidth + width + x - iconX) * progress * (factor * factor * factor / cube);
                var pull = Pull(offset, geoX - iconWidth + width - iconX - (width - x));
                outY = ((iconY + (iconHeight * (y / height)) - (y + geoY)) * pull) + y;
                outX = Math.Max(minX, x - offset);
                return;
            }

            default:
            {
                var cube = width * width * width;
                var maxX = iconX - geoX;
                var factor = x + ((width - x) * progress);
                var offset = (iconX + x - geoX) * progress * (factor * factor * factor / cube);
                var pull = Pull(offset, iconX - geoX - x);
                outY = ((iconY + (iconHeight * (y / height)) - (y + geoY)) * pull) + y;
                outX = Math.Min(maxX, x + offset);
                return;
            }
        }
    }
}
