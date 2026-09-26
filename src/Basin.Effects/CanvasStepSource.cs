using Basin.Scene;

namespace Basin.Effects;

public sealed class CanvasStepSource : IMeshSource
{
    private static readonly double[] Quarters = [0.25, 0.5, 0.75];

    private static readonly CanvasStepSurfaceSource Plain = new(CanvasStepSurface.Walls);

    private static readonly CanvasStepSides[] Order =
        [CanvasStepSides.Top, CanvasStepSides.Left, CanvasStepSides.Right, CanvasStepSides.Bottom];

    private int _cellSize = 64;
    private float _alpha = 1f;
    private double _minSpacing;

    public CanvasStepMap? Map { get; set; }

    public int CellSize
    {
        get => _cellSize;
        set => _cellSize = Math.Max(2, value);
    }

    public double MinLineSpacing
    {
        get => _minSpacing;
        set => _minSpacing = Math.Max(0.0, value);
    }

    public RenderColor Color { get; set; } = new(0.16f, 0.21f, 0.75f, 1f);

    public CanvasStepSurfaceSource? Walls { get; set; }

    public float Alpha
    {
        get => _alpha;
        set => _alpha = Math.Clamp(value, 0f, 1f);
    }

    public bool Lines { get; set; } = true;

    public bool ShelfLines { get; set; } = true;

    public bool WallGridLines { get; set; } = true;

    public bool DesktopLines { get; set; } = true;

    public int VertexCount(in Box bounds) => Walk(bounds, [], write: false);

    public void WriteVertices(in Box bounds, Span<MeshVertex> into) => _ = Walk(bounds, into, write: true);

    private int Walk(in Box bounds, Span<MeshVertex> into, bool write)
    {
        if (Map is not { } map || bounds.IsEmpty || map.IsIdentity)
        {
            return 0;
        }

        var count = 0;
        var lines = Lines && _alpha > 0f;
        if (lines)
        {
            var color = new RenderColor(Color.R * _alpha, Color.G * _alpha, Color.B * _alpha, Color.A * _alpha);
            if (ShelfLines)
            {
                ShelfGrid(map, bounds, into, ref count, write, color);
            }

            if (DesktopLines)
            {
                Grid(map, CanvasStepPlane.Desktop, Clip(map.Outline, bounds), into, ref count, write, color);
            }

            foreach (var side in Order)
            {
                if (WallGridLines && map.WallWidth(side) > 0)
                {
                    WallLines(map, side, into, ref count, write, color);
                }
            }
        }

        foreach (var side in Order)
        {
            if (map.WallWidth(side) > 0)
            {
                BaseLine(map, side, into, ref count, write);
            }
        }

        return count;
    }

    private void BaseLine(CanvasStepMap map, CanvasStepSides side, Span<MeshVertex> into, ref int count, bool write)
    {
        if (write)
        {
            CanvasStepSurfaceSource.Corners(map, side, out _, out _, out var outer0, out var outer1);
            var color = Darken((Walls ?? Plain).BaseColorOf(side), 0.5);
            var horizontal = side is CanvasStepSides.Top or CanvasStepSides.Bottom;
            var inward = side is CanvasStepSides.Left or CanvasStepSides.Top ? 0.0 : -1.0;
            if (horizontal)
            {
                var y = Math.Round(outer0.Y) + inward;
                Quad(into.Slice(count, 6), outer0.X, y, outer1.X, y + 1, color);
            }
            else
            {
                var x = Math.Round(outer0.X) + inward;
                Quad(into.Slice(count, 6), x, outer0.Y, x + 1, outer1.Y, color);
            }
        }

        count += 6;
    }

    private void WallLines(CanvasStepMap map, CanvasStepSides side, Span<MeshVertex> into, ref int count, bool write, in RenderColor color)
    {
        CanvasStepSurfaceSource.Corners(map, side, out var inner0, out var inner1, out var outer0, out var outer1);
        foreach (var quarter in Quarters)
        {
            if (write)
            {
                var from = Lerp(inner0, outer0, quarter);
                var to = Lerp(inner1, outer1, quarter);
                Segment(into.Slice(count, 6), from, to, color);
            }

            count += 6;
        }

        var horizontal = side is CanvasStepSides.Top or CanvasStepSides.Bottom;
        var d = map.Outline;
        var start = horizontal ? d.X : d.Y;
        var end = horizontal ? d.Right : d.Bottom;
        var center = horizontal ? map.CenterX : map.CenterY;
        var stride = Stride(map.Zoom);
        var step = (long)_cellSize * stride;
        var first = Ceiling((long)Math.Ceiling(center + ((start - center) / map.Zoom)), step);
        var last = Floor((long)Math.Floor(center + ((end - center) / map.Zoom)), step);
        for (var canvas = first; canvas <= last; canvas += step)
        {
            var screen = Math.Round(center + ((canvas - center) * map.Zoom));
            if (screen < start || screen > end)
            {
                continue;
            }

            if (write)
            {
                var t = (screen - start) / (end - start);
                var from = horizontal ? (screen, inner0.Y) : (inner0.X, screen);
                var to = Lerp(outer0, outer1, t);
                Segment(into.Slice(count, 6), from, to, color);
            }

            count += 6;
        }
    }

    private void ShelfGrid(CanvasStepMap map, in Box bounds, Span<MeshVertex> into, ref int count, bool write, in RenderColor color)
    {
        var u = map.Outer;
        var b = map.Inner;
        var sides = map.Sides;
        var left = (sides & CanvasStepSides.Left) != 0;
        var right = (sides & CanvasStepSides.Right) != 0;
        if (left)
        {
            Grid(map, CanvasStepPlane.Shelf, Clip(Span(u.X, u.Y, b.X, u.Bottom), bounds), into, ref count, write, color);
        }

        if (right)
        {
            Grid(map, CanvasStepPlane.Shelf, Clip(Span(b.Right, u.Y, u.Right, u.Bottom), bounds), into, ref count, write, color);
        }

        var x0 = left ? b.X : u.X;
        var x1 = right ? b.Right : u.Right;
        if ((sides & CanvasStepSides.Top) != 0)
        {
            Grid(map, CanvasStepPlane.Shelf, Clip(Span(x0, u.Y, x1, b.Y), bounds), into, ref count, write, color);
        }

        if ((sides & CanvasStepSides.Bottom) != 0)
        {
            Grid(map, CanvasStepPlane.Shelf, Clip(Span(x0, b.Bottom, x1, u.Bottom), bounds), into, ref count, write, color);
        }
    }

    private void Grid(CanvasStepMap map, CanvasStepPlane plane, in FBox area, Span<MeshVertex> into, ref int count, bool write, in RenderColor color)
    {
        if (area.Width < 1 || area.Height < 1)
        {
            return;
        }

        var scale = map.ScaleOf(plane);
        var step = (long)_cellSize * Stride(scale);
        var firstX = Ceiling((long)Math.Ceiling(map.CenterX + ((area.X - map.CenterX) / scale)), step);
        var lastX = Floor((long)Math.Floor(map.CenterX + ((area.Right - map.CenterX) / scale)), step);
        for (var canvas = firstX; canvas <= lastX; canvas += step)
        {
            var x = Math.Round(map.CenterX + ((canvas - map.CenterX) * scale));
            if (x < area.X || x >= area.Right)
            {
                continue;
            }

            if (write)
            {
                Quad(into.Slice(count, 6), x, area.Y, x + 1, area.Bottom, color);
            }

            count += 6;
        }

        var firstY = Ceiling((long)Math.Ceiling(map.CenterY + ((area.Y - map.CenterY) / scale)), step);
        var lastY = Floor((long)Math.Floor(map.CenterY + ((area.Bottom - map.CenterY) / scale)), step);
        for (var canvas = firstY; canvas <= lastY; canvas += step)
        {
            var y = Math.Round(map.CenterY + ((canvas - map.CenterY) * scale));
            if (y < area.Y || y >= area.Bottom)
            {
                continue;
            }

            if (write)
            {
                Quad(into.Slice(count, 6), area.X, y, area.Right, y + 1, color);
            }

            count += 6;
        }
    }

    private int Stride(double scale)
    {
        var stride = 1;
        while (_minSpacing > 0 && scale * _cellSize * stride < _minSpacing && stride < 1 << 20)
        {
            stride <<= 1;
        }

        return stride;
    }

    private static FBox Span(double left, double top, double right, double bottom) =>
        new(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));

    private static FBox Clip(in FBox area, in Box bounds)
    {
        var left = Math.Max(area.X, bounds.X);
        var top = Math.Max(area.Y, bounds.Y);
        var right = Math.Min(area.Right, bounds.Right);
        var bottom = Math.Min(area.Bottom, bounds.Bottom);
        return Span(left, top, right, bottom);
    }

    private static (double X, double Y) Lerp((double X, double Y) from, (double X, double Y) to, double t) =>
        (from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));

    private static void Segment(Span<MeshVertex> into, (double X, double Y) from, (double X, double Y) to, in RenderColor color)
    {
        if (Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y))
        {
            MeshGrid.WriteCell(
                into, 0, 0, 0, 0,
                ((float)from.X, (float)from.Y), ((float)to.X, (float)to.Y),
                ((float)to.X, (float)to.Y + 1), ((float)from.X, (float)from.Y + 1), color);
            return;
        }

        MeshGrid.WriteCell(
            into, 0, 0, 0, 0,
            ((float)from.X, (float)from.Y), ((float)from.X + 1, (float)from.Y),
            ((float)to.X + 1, (float)to.Y), ((float)to.X, (float)to.Y), color);
    }

    private static void Quad(Span<MeshVertex> into, double left, double top, double right, double bottom, in RenderColor color) =>
        MeshGrid.WriteCell(
            into, 0, 0, 0, 0,
            ((float)left, (float)top), ((float)right, (float)top), ((float)right, (float)bottom), ((float)left, (float)bottom), color);

    private static RenderColor Darken(in RenderColor color, double amount)
    {
        var keep = 1f - (float)amount;
        return new RenderColor(color.R * keep, color.G * keep, color.B * keep, color.A);
    }

    private static long Ceiling(long value, long cell)
    {
        var remainder = value % cell;
        return remainder == 0 ? value : remainder > 0 ? value + (cell - remainder) : value - remainder;
    }

    private static long Floor(long value, long cell)
    {
        var remainder = value % cell;
        return remainder == 0 ? value : remainder > 0 ? value - remainder : value - (cell + remainder);
    }
}
