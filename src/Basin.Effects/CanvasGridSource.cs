using Basin.Scene;

namespace Basin.Effects;

public sealed class CanvasGridSource : IMeshSource
{
    private const int SegmentWidth = 8;

    private readonly CanvasWarpTransform _map = new();
    private int _cellSize = 64;
    private float _alpha = 1f;

    public CanvasWarp? Left
    {
        get => _map.Left;
        set => _map.Left = value;
    }

    public CanvasWarp? Right
    {
        get => _map.Right;
        set => _map.Right = value;
    }

    public CanvasWarp? Top
    {
        get => _map.Top;
        set => _map.Top = value;
    }

    public CanvasWarp? Bottom
    {
        get => _map.Bottom;
        set => _map.Bottom = value;
    }

    public double CornerRadius
    {
        get => _map.CornerRadius;
        set => _map.CornerRadius = value;
    }

    public bool CornerTaper
    {
        get => _map.CornerTaper;
        set => _map.CornerTaper = value;
    }

    public int CellSize
    {
        get => _cellSize;
        set => _cellSize = Math.Max(2, value);
    }

    public RenderColor Color { get; set; } = new(0.16f, 0.21f, 0.75f, 1f);

    public float Alpha
    {
        get => _alpha;
        set => _alpha = Math.Clamp(value, 0f, 1f);
    }

    public double ToScreen(double canvasX) => _map.ToScreen(canvasX);

    public double ToCanvas(double screenX) => _map.ToCanvas(screenX);

    public double ToScreenY(double canvasY)
    {
        if (Top is { } top && top.ContainsCanvas(canvasY))
        {
            return top.ToScreen(canvasY);
        }

        if (Bottom is { } bottom && bottom.ContainsCanvas(canvasY))
        {
            return bottom.ToScreen(canvasY);
        }

        return canvasY;
    }

    public double ToCanvasY(double screenY)
    {
        if (Top is { } top && top.ContainsScreen(screenY))
        {
            return top.ToCanvas(screenY);
        }

        if (Bottom is { } bottom && bottom.ContainsScreen(screenY))
        {
            return bottom.ToCanvas(screenY);
        }

        return screenY;
    }

    public int VerticalLines(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        var first = FirstVertical(bounds);
        var last = LastVertical(bounds);
        return last < first ? 0 : (int)((last - first) / _cellSize) + 1;
    }

    public int HorizontalLines(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        var first = FirstHorizontal(bounds);
        var last = LastHorizontal(bounds);
        return last < first ? 0 : (int)((last - first) / _cellSize) + 1;
    }

    public int ColumnSegments(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        return 1 + CornerSegments(Top) + CornerSegments(Bottom);
    }

    public int RowSegments(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        return 1 + ZoneSegments(Left, bounds.X, bounds.Right) + ZoneSegments(Right, bounds.X, bounds.Right);
    }

    public int ZoneRowSegments(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        return 1 + CornerSegments(Left) + CornerSegments(Right);
    }

    public int VertexCount(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        var rows = HorizontalLines(bounds);
        var zoneRows = ZoneRows(bounds, rows);
        return ((VerticalLines(bounds) * ColumnSegments(bounds))
            + ((rows - zoneRows) * RowSegments(bounds))
            + (zoneRows * ZoneRowSegments(bounds))) * 6;
    }

    public void WriteVertices(in Box bounds, Span<MeshVertex> into)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        var color = new RenderColor(Color.R * _alpha, Color.G * _alpha, Color.B * _alpha, Color.A * _alpha);
        var write = 0;
        var first = FirstVertical(bounds);
        var count = VerticalLines(bounds);
        var topActive = Top is { IsIdentity: false };
        var bottomActive = Bottom is { IsIdentity: false };
        for (var i = 0; i < count; i++)
        {
            var canvasX = (double)(first + ((long)i * _cellSize));
            var exact = ToScreen(canvasX);
            var x = (float)Math.Round(exact);
            var shift = x - exact;
            var top = topActive ? (float)_map.ToScreenPoint(canvasX, Top!.Seam).Y : bounds.Y;
            var bottom = bottomActive ? (float)_map.ToScreenPoint(canvasX, Bottom!.Seam).Y : bounds.Bottom;
            WriteColumn(into, ref write, (x, top), (x, bottom), color);
            if (topActive)
            {
                WriteColumnCorner(into, ref write, Top!, canvasX, shift, color);
            }

            if (bottomActive)
            {
                WriteColumnCorner(into, ref write, Bottom!, canvasX, shift, color);
            }
        }

        var firstRow = FirstHorizontal(bounds);
        var rows = HorizontalLines(bounds);
        var leftSegments = ZoneSegments(Left, bounds.X, bounds.Right);
        var rightSegments = ZoneSegments(Right, bounds.X, bounds.Right);
        var flatLeft = leftSegments > 0 ? Left!.Seam : bounds.X;
        var flatRight = rightSegments > 0 ? Right!.Seam : bounds.Right;
        var leftActive = Left is { IsIdentity: false };
        var rightActive = Right is { IsIdentity: false };
        for (var i = 0; i < rows; i++)
        {
            var y = firstRow + ((long)i * _cellSize);
            if (EndAt(y) is { } end)
            {
                var screenY = end.ToScreen(y);
                var start = leftActive ? _map.ToScreenPoint(Left!.Seam, y) : (bounds.X, screenY);
                var stop = rightActive ? _map.ToScreenPoint(Right!.Seam, y) : (bounds.Right, screenY);
                WriteRow(into, ref write, start, stop, color);
                if (leftActive)
                {
                    WriteRowCorner(into, ref write, Left!, end, y, color);
                }

                if (rightActive)
                {
                    WriteRowCorner(into, ref write, Right!, end, y, color);
                }

                continue;
            }

            WriteRow(into, ref write, flatLeft, flatRight, y, color);
            for (var segment = 0; segment < leftSegments; segment++)
            {
                var x1 = Left!.Seam - (segment * SegmentWidth);
                var x0 = Math.Max(bounds.X, x1 - SegmentWidth);
                WriteRow(into, ref write, x0, x1, y, color);
            }

            for (var segment = 0; segment < rightSegments; segment++)
            {
                var x0 = Right!.Seam + (segment * SegmentWidth);
                var x1 = Math.Min(bounds.Right, x0 + SegmentWidth);
                WriteRow(into, ref write, x0, x1, y, color);
            }
        }
    }

    private static int ZoneSegments(CanvasWarp? warp, int boundsStart, int boundsEnd)
    {
        if (warp is not { IsIdentity: false } zone || zone.Slope == 0)
        {
            return 0;
        }

        var start = Math.Max(boundsStart, Math.Min(zone.Seam, zone.ScreenEdge));
        var end = Math.Min(boundsEnd, Math.Max(zone.Seam, zone.ScreenEdge));
        return end <= start ? 0 : (end - start + SegmentWidth - 1) / SegmentWidth;
    }

    private static int CornerSegments(CanvasWarp? warp) =>
        warp is { IsIdentity: false } zone ? Math.Max(1, (zone.ZoneWidth + SegmentWidth - 1) / SegmentWidth) : 0;

    private int ZoneRows(in Box bounds, int rows)
    {
        if (Top is not { IsIdentity: false } && Bottom is not { IsIdentity: false })
        {
            return 0;
        }

        var first = FirstHorizontal(bounds);
        var zoneRows = 0;
        for (var i = 0; i < rows; i++)
        {
            if (EndAt(first + ((long)i * _cellSize)) is not null)
            {
                zoneRows++;
            }
        }

        return zoneRows;
    }

    private CanvasWarp? EndAt(double canvasY) =>
        Top is { } top && top.ContainsCanvas(canvasY) ? top
        : Bottom is { } bottom && bottom.ContainsCanvas(canvasY) ? bottom
        : null;

    private static double Across(CanvasWarp? warp, double canvas) =>
        warp is { IsIdentity: false } && warp.ContainsCanvas(canvas)
            ? Math.Min(1.0, warp.Direction * (canvas - warp.Seam) / warp.Extension)
            : 0.0;

    private void WriteColumnCorner(Span<MeshVertex> into, ref int write, CanvasWarp end, double canvasX, double shift, in RenderColor color)
    {
        var across = Math.Max(Across(Left, canvasX), Across(Right, canvasX));
        var reach = _map.CornerReach(across);
        var depth = end.DepthAt(end.Seam + (end.Direction * reach * end.Extension));
        var segments = CornerSegments(end);
        var crease = CreaseStep(end, across, reach, depth, segments);
        var previous = ColumnPoint(end, canvasX, shift, 0.0);
        for (var j = 1; j <= segments; j++)
        {
            var next = ColumnPoint(end, canvasX, shift, j == crease ? CreaseDepth(end, across) : depth * j / segments);
            WriteColumn(into, ref write, previous, next, color);
            previous = next;
        }
    }

    private (float X, float Y) ColumnPoint(CanvasWarp end, double canvasX, double shift, double depth)
    {
        var canvasY = end.ToCanvas(end.Seam + (end.Direction * depth * end.ZoneWidth));
        var (x, y) = _map.ToScreenPoint(canvasX, canvasY);
        return ((float)(x + shift), (float)y);
    }

    private void WriteRowCorner(Span<MeshVertex> into, ref int write, CanvasWarp side, CanvasWarp end, double canvasY, in RenderColor color)
    {
        var across = Math.Min(1.0, end.Direction * (canvasY - end.Seam) / end.Extension);
        var reach = _map.CornerReach(across);
        var depth = side.DepthAt(side.Seam + (side.Direction * reach * side.Extension));
        var segments = CornerSegments(side);
        var crease = CreaseStep(side, across, reach, depth, segments);
        var previous = RowPoint(side, canvasY, 0.0);
        for (var j = 1; j <= segments; j++)
        {
            var next = RowPoint(side, canvasY, j == crease ? CreaseDepth(side, across) : depth * j / segments);
            WriteRow(into, ref write, previous, next, color);
            previous = next;
        }
    }

    private int CreaseStep(CanvasWarp warp, double across, double reach, double depth, int segments)
    {
        if (_map.CornerRadius > 0 || across <= 0 || across >= reach || depth <= 0 || segments < 2)
        {
            return -1;
        }

        var step = (int)Math.Round(CreaseDepth(warp, across) / depth * segments);
        return Math.Clamp(step, 1, segments - 1);
    }

    private static double CreaseDepth(CanvasWarp warp, double across) =>
        warp.DepthAt(warp.Seam + (warp.Direction * across * warp.Extension));

    private (double X, double Y) RowPoint(CanvasWarp side, double canvasY, double depth)
    {
        var canvasX = side.ToCanvas(side.Seam + (side.Direction * depth * side.ZoneWidth));
        return _map.ToScreenPoint(canvasX, canvasY);
    }

    private double CenterAt(double screenX)
    {
        if (Left is { } left && left.ContainsScreen(screenX))
        {
            return left.Center;
        }

        if (Right is { } right && right.ContainsScreen(screenX))
        {
            return right.Center;
        }

        return 0.0;
    }

    private double RowY(double screenX, double y)
    {
        var fan = _map.FanAtScreen(screenX);
        if (fan == 1.0)
        {
            return y;
        }

        var center = CenterAt(screenX);
        return center + ((y - center) * fan);
    }

    private static void WriteColumn(Span<MeshVertex> into, ref int write, (float X, float Y) from, (float X, float Y) to, in RenderColor color)
    {
        MeshGrid.WriteCell(
            into.Slice(write, 6), 0, 0, 0, 0,
            (from.X, from.Y), (from.X + 1, from.Y), (to.X + 1, to.Y), (to.X, to.Y), color);
        write += 6;
    }

    private static void WriteRow(Span<MeshVertex> into, ref int write, (double X, double Y) from, (double X, double Y) to, in RenderColor color)
    {
        var x0 = (float)from.X;
        var y0 = (float)from.Y;
        var x1 = (float)to.X;
        var y1 = (float)to.Y;
        MeshGrid.WriteCell(
            into.Slice(write, 6), 0, 0, 0, 0,
            (x0, y0), (x1, y1), (x1, y1 + 1), (x0, y0 + 1), color);
        write += 6;
    }

    private void WriteRow(Span<MeshVertex> into, ref int write, int x0, int x1, long y, in RenderColor color)
    {
        var y0 = (float)RowY(x0, y);
        var y1 = (float)RowY(x1, y);
        MeshGrid.WriteCell(
            into.Slice(write, 6), 0, 0, 0, 0,
            (x0, y0), (x1, y1), (x1, y1 + 1), (x0, y0 + 1), color);
        write += 6;
    }

    private long FirstHorizontal(in Box bounds) => Ceiling((long)Math.Ceiling(ToCanvasY(bounds.Y)), _cellSize);

    private long LastHorizontal(in Box bounds) => Floor((long)Math.Floor(ToCanvasY(bounds.Bottom - 1)), _cellSize);

    private long FirstVertical(in Box bounds) => Ceiling((long)Math.Ceiling(ToCanvas(bounds.X)), _cellSize);

    private long LastVertical(in Box bounds) => Floor((long)Math.Floor(ToCanvas(bounds.Right - 1)), _cellSize);

    private static long Ceiling(long value, int cell)
    {
        var remainder = value % cell;
        return remainder == 0 ? value : remainder > 0 ? value + (cell - remainder) : value - remainder;
    }

    private static long Floor(long value, int cell)
    {
        var remainder = value % cell;
        return remainder == 0 ? value : remainder > 0 ? value - remainder : value - (cell + remainder);
    }
}
