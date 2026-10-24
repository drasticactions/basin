using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// The mesh source for the grid lines of a step map: the desktop grid, the shelf grid, the lines across each wall and the base lines.
/// </summary>
public sealed class CanvasStepSource : IMeshSource
{
    private static readonly double[] Quarters = [0.25, 0.5, 0.75];

    private static readonly CanvasStepSurfaceSource Plain = new(CanvasStepSurface.Walls);

    private static readonly CanvasStepSides[] Order =
        [CanvasStepSides.Top, CanvasStepSides.Left, CanvasStepSides.Right, CanvasStepSides.Bottom];

    private int _cellSize = 64;
    private float _alpha = 1f;
    private double _minSpacing;
    private GridReveal _reveal = GridReveal.Full;
    private GridRevealMask _mask;
    private int _lineVertices = 6;
    private GridTone _tone;
    private bool _segments;
    private int _factor = 1;
    private bool _toned;
    private HaloPart? _glow;

    /// <summary>
    /// Gets or sets the step map to draw. With no map or an identity map the source writes nothing.
    /// </summary>
    public CanvasStepMap? Map { get; set; }

    /// <summary>
    /// Gets or sets the grid cell in canvas pixels. The setter keeps it at 2 or above.
    /// </summary>
    public int CellSize
    {
        get => _cellSize;
        set => _cellSize = Math.Max(2, value);
    }

    /// <summary>
    /// Gets or sets the least screen spacing between drawn lines. A denser grid draws only every second, fourth or later line. Zero draws every line.
    /// </summary>
    public double MinLineSpacing
    {
        get => _minSpacing;
        set => _minSpacing = Math.Max(0.0, value);
    }

    /// <summary>
    /// Gets or sets the straight line color, before <see cref="Alpha"/>.
    /// </summary>
    public RenderColor Color { get; set; } = new(0.16f, 0.21f, 0.75f, 1f);

    /// <summary>
    /// Gets or sets the wall source whose colors the base lines darken. When it is <see langword="null"/>, the base lines use the default wall colors.
    /// </summary>
    public CanvasStepSurfaceSource? Walls { get; set; }

    /// <summary>
    /// Gets or sets the fade of the grid lines, from 0 to 1. It does not fade the base lines. The setter clamps the value.
    /// </summary>
    public float Alpha
    {
        get => _alpha;
        set => _alpha = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Gets or sets the reveal animation of the shelf, desktop and wall lines. It does not cover the base lines.
    /// </summary>
    public GridReveal Reveal
    {
        get => _reveal;
        set => _reveal = value;
    }

    /// <summary>
    /// Gets or sets the highlight sources near windows and the pointer.
    /// </summary>
    public GridLights Lights { get; set; } = new();

    /// <summary>
    /// Gets or sets the density dimming and the glow. The default is no effect.
    /// </summary>
    public GridEffects Effects { get; set; } = GridEffects.None;

    /// <summary>
    /// Gets or sets a value indicating whether the source draws any grid line. The base lines draw either way.
    /// </summary>
    public bool Lines { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the source draws the shelf grid.
    /// </summary>
    public bool ShelfLines { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the source draws the lines across the walls.
    /// </summary>
    public bool WallGridLines { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the source draws the desktop grid.
    /// </summary>
    public bool DesktopLines { get; set; } = true;

    /// <summary>
    /// Gets the second mesh source that writes the glow halo beside each line. It writes nothing unless <see cref="Effects"/> asks for the mesh halo. Draw it with additive blending.
    /// </summary>
    public IMeshSource Glow => _glow ??= new HaloPart(this);

    /// <inheritdoc/>
    public int VertexCount(in Box bounds) => Walk(bounds, [], write: false, halo: false);

    /// <inheritdoc/>
    public void WriteVertices(in Box bounds, Span<MeshVertex> into) => _ = Walk(bounds, into, write: true, halo: false);

    private sealed class HaloPart(CanvasStepSource grid) : IMeshSource
    {
        public int VertexCount(in Box bounds) => grid.Effects.DrawsHalo ? grid.Walk(bounds, [], write: false, halo: true) : 0;

        public void WriteVertices(in Box bounds, Span<MeshVertex> into)
        {
            if (grid.Effects.DrawsHalo)
            {
                _ = grid.Walk(bounds, into, write: true, halo: true);
            }
        }
    }

    private int Walk(in Box bounds, Span<MeshVertex> into, bool write, bool halo)
    {
        if (Map is not { } map || bounds.IsEmpty || map.IsIdentity)
        {
            return 0;
        }

        var count = 0;
        _mask = write ? GridRevealMask.Of(_reveal, bounds) : default;
        _lineVertices = GridRevealMask.ClipsFor(_reveal) ? GridRevealMask.LineVertices : 6;
        var effects = Effects;
        _segments = effects.Density || Lights.IsLit;
        _factor = halo ? 2 : 1;
        _tone = GridTone.Of(Color, _alpha, write ? _mask.Scale : 1f, Lights, effects);
        _tone.HaloPart = halo;
        _toned = halo || _tone.Segments;
        var lines = Lines && _alpha > 0f;
        if (lines)
        {
            var alpha = _alpha * (write ? _mask.Scale : 1f);
            var color = new RenderColor(Color.R * alpha, Color.G * alpha, Color.B * alpha, Color.A * alpha);
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
            if (!halo && map.WallWidth(side) > 0 && (map.Shelves & side) != 0)
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
        var wallDensity = _tone.DensityOf(map.WallWidth(side) / (Quarters.Length + 1));
        foreach (var quarter in Quarters)
        {
            var from = map.Bend(side, Lerp(inner0, outer0, quarter).X, Lerp(inner0, outer0, quarter).Y, quarter);
            var to = map.Bend(side, Lerp(inner1, outer1, quarter).X, Lerp(inner1, outer1, quarter).Y, quarter);
            var pieces = Pieces(from, to);
            if (write)
            {
                Line(into, count, from, to, color, pieces, wallDensity, wallDensity);
            }

            count += pieces * _lineVertices * _factor;
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
        var innerLength = Distance(inner0, inner1);
        var fan = innerLength > 0 ? Distance(outer0, outer1) / innerLength : 1.0;
        var innerDensity = _tone.DensityOf(step * map.Zoom);
        var outerDensity = _tone.DensityOf(step * map.Zoom * fan);
        for (var canvas = first; canvas <= last; canvas += step)
        {
            var screen = Math.Round(center + ((canvas - center) * map.Zoom));
            if (screen < start || screen > end)
            {
                continue;
            }

            var t = (screen - start) / (end - start);
            var from = horizontal ? (screen, inner0.Y) : (inner0.X, screen);
            var to = Lerp(outer0, outer1, t);
            var pieces = map.Bulges ? GridTone.Pieces(Distance(from, to), segments: true) : Pieces(from, to);
            if (write)
            {
                Line(into, count, from, to, color, pieces, innerDensity, outerDensity, map.Bulges ? side : CanvasStepSides.None);
            }

            count += pieces * _lineVertices * _factor;
        }
    }

    private void ShelfGrid(CanvasStepMap map, in Box bounds, Span<MeshVertex> into, ref int count, bool write, in RenderColor color)
    {
        var u = map.Outer;
        var b = map.Inner;
        var sides = map.Shelves;
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
        var density = _tone.DensityOf(step * scale);
        var columnPieces = GridTone.Pieces(area.Height, _segments);
        var rowPieces = GridTone.Pieces(area.Width, _segments);
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
                Column(into, count, x, area.Y, area.Bottom, color, columnPieces, density);
            }

            count += columnPieces * _lineVertices * _factor;
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
                Row(into, count, area.X, area.Right, y, color, rowPieces, density);
            }

            count += rowPieces * _lineVertices * _factor;
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

    private int Pieces((double X, double Y) from, (double X, double Y) to) =>
        GridTone.Pieces(Distance(from, to), _segments);

    private static double Distance((double X, double Y) from, (double X, double Y) to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private void Column(Span<MeshVertex> into, int at, double x, double top, double bottom, in RenderColor color, int pieces, float density)
    {
        if (_toned)
        {
            Toned(into, at, (x, top), (x, bottom), 1, 0, vertical: true, pieces, density, density);
            return;
        }

        if (!_mask.Clips)
        {
            Quad(into.Slice(at, 6), x, top, x + 1, bottom, color);
            return;
        }

        _mask.WriteLine(into.Slice(at, _lineVertices), ((float)x, (float)top), ((float)x, (float)bottom), 1, 0, vertical: true, color);
    }

    private void Row(Span<MeshVertex> into, int at, double left, double right, double y, in RenderColor color, int pieces, float density)
    {
        if (_toned)
        {
            Toned(into, at, (left, y), (right, y), 0, 1, vertical: false, pieces, density, density);
            return;
        }

        if (!_mask.Clips)
        {
            Quad(into.Slice(at, 6), left, y, right, y + 1, color);
            return;
        }

        _mask.WriteLine(into.Slice(at, _lineVertices), ((float)left, (float)y), ((float)right, (float)y), 0, 1, vertical: false, color);
    }

    private void Line(
        Span<MeshVertex> into, int at, (double X, double Y) from, (double X, double Y) to, in RenderColor color,
        int pieces, float fromDensity, float toDensity, CanvasStepSides bend = CanvasStepSides.None)
    {
        var horizontal = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
        if (_toned || bend != CanvasStepSides.None)
        {
            Toned(into, at, from, to, horizontal ? 0 : 1, horizontal ? 1 : 0, vertical: !horizontal, pieces, fromDensity, toDensity, bend);
            return;
        }

        if (!_mask.Clips)
        {
            Segment(into.Slice(at, 6), from, to, color);
            return;
        }

        _mask.WriteLine(
            into.Slice(at, _lineVertices), ((float)from.X, (float)from.Y), ((float)to.X, (float)to.Y),
            horizontal ? 0 : 1, horizontal ? 1 : 0, vertical: !horizontal, color);
    }

    private void Toned(
        Span<MeshVertex> into, int at, (double X, double Y) from, (double X, double Y) to, float nx, float ny, bool vertical,
        int pieces, float fromDensity, float toDensity, CanvasStepSides bend = CanvasStepSides.None)
    {
        pieces = Math.Max(1, pieces);
        var write = at;
        var previous = ((float)from.X, (float)from.Y);
        for (var i = 1; i <= pieces; i++)
        {
            var t = (double)i / pieces;
            var next = i == pieces ? ((float)to.X, (float)to.Y) : ((float)(from.X + ((to.X - from.X) * t)), (float)(from.Y + ((to.Y - from.Y) * t)));
            if (bend != CanvasStepSides.None && Map is { } map)
            {
                var (bentX, bentY) = map.Bend(bend, next.Item1, next.Item2, t);
                next = ((float)bentX, (float)bentY);
            }
            var start = fromDensity + ((toDensity - fromDensity) * (float)((i - 1.0) / pieces));
            var stop = fromDensity + ((toDensity - fromDensity) * (float)t);
            write += GridQuad.Write(into[write..], _mask, clipLine: true, previous, next, nx, ny, vertical, _tone, start, stop);
            previous = next;
        }
    }

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
