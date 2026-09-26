using Basin.Scene;

namespace Basin.Effects;

public sealed class CanvasGridSource : IMeshSource
{
    private const int SegmentWidth = 8;

    private readonly CanvasWarpTransform _map = new();
    private int _cellSize = 64;
    private float _alpha = 1f;
    private double _minSpacing;

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

    public bool Separable
    {
        get => _map.Separable;
        set => _map.Separable = value;
    }

    public double MinLineSpacing
    {
        get => _minSpacing;
        set => _minSpacing = Math.Max(0.0, value);
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
        var total = Candidates(first, LastVertical(bounds));
        if (_minSpacing <= 0)
        {
            return total;
        }

        var count = 0;
        for (var i = 0; i < total; i++)
        {
            if (DrawsColumn(first + ((long)i * _cellSize)))
            {
                count++;
            }
        }

        return count;
    }

    public int HorizontalLines(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        var first = FirstHorizontal(bounds);
        var total = Candidates(first, LastHorizontal(bounds));
        if (_minSpacing <= 0)
        {
            return total;
        }

        var count = 0;
        for (var i = 0; i < total; i++)
        {
            if (DrawsRow(first + ((long)i * _cellSize)))
            {
                count++;
            }
        }

        return count;
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

        return 1 + ZoneSegments(Left, bounds.X, bounds.Right) + ZoneSegments(Right, bounds.X, bounds.Right)
            + ShelfSegment(Left, bounds) + ShelfSegment(Right, bounds);
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

        if (Separable)
        {
            return (VerticalLines(bounds) + HorizontalLines(bounds)) * 6;
        }

        var count = VerticalLines(bounds) * ColumnSegments(bounds);
        var zoneRowSegments = ZoneRowSegments(bounds);
        var rowSegments = 1 + ZoneSegments(Left, bounds.X, bounds.Right) + ZoneSegments(Right, bounds.X, bounds.Right);
        var leftShelf = ShelfSegment(Left, bounds);
        var rightShelf = ShelfSegment(Right, bounds);
        var first = FirstHorizontal(bounds);
        var total = Candidates(first, LastHorizontal(bounds));
        for (var i = 0; i < total; i++)
        {
            var y = first + ((long)i * _cellSize);
            if (!DrawsRow(y))
            {
                continue;
            }

            if (EndAt(y) is not null)
            {
                count += zoneRowSegments;
                continue;
            }

            count += rowSegments;
            if (leftShelf > 0 && DrawsShelfRow(Left!, y))
            {
                count++;
            }

            if (rightShelf > 0 && DrawsShelfRow(Right!, y))
            {
                count++;
            }
        }

        return count * 6;
    }

    public void WriteVertices(in Box bounds, Span<MeshVertex> into)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        var color = new RenderColor(Color.R * _alpha, Color.G * _alpha, Color.B * _alpha, Color.A * _alpha);
        var write = 0;
        if (Separable)
        {
            WriteStraight(bounds, into, color);
            return;
        }

        var first = FirstVertical(bounds);
        var count = Candidates(first, LastVertical(bounds));
        var topActive = Top is { IsIdentity: false };
        var bottomActive = Bottom is { IsIdentity: false };
        for (var i = 0; i < count; i++)
        {
            var column = first + ((long)i * _cellSize);
            if (!DrawsColumn(column))
            {
                continue;
            }

            var canvasX = (double)column;
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
        var rows = Candidates(firstRow, LastHorizontal(bounds));
        var leftShelf = ShelfSegment(Left, bounds) > 0;
        var rightShelf = ShelfSegment(Right, bounds) > 0;
        var leftSegments = ZoneSegments(Left, bounds.X, bounds.Right);
        var rightSegments = ZoneSegments(Right, bounds.X, bounds.Right);
        var flatLeft = leftSegments > 0 ? Left!.Seam : bounds.X;
        var flatRight = rightSegments > 0 ? Right!.Seam : bounds.Right;
        var leftActive = Left is { IsIdentity: false };
        var rightActive = Right is { IsIdentity: false };
        for (var i = 0; i < rows; i++)
        {
            var y = firstRow + ((long)i * _cellSize);
            if (!DrawsRow(y))
            {
                continue;
            }

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
                var x0 = Math.Max(Left.Terrace ? Math.Max(bounds.X, Left.Foot) : bounds.X, x1 - SegmentWidth);
                WriteRow(into, ref write, x0, x1, y, color);
            }

            for (var segment = 0; segment < rightSegments; segment++)
            {
                var x0 = Right!.Seam + (segment * SegmentWidth);
                var x1 = Math.Min(Right.Terrace ? Math.Min(bounds.Right, Right.Foot) : bounds.Right, x0 + SegmentWidth);
                WriteRow(into, ref write, x0, x1, y, color);
            }

            if (leftShelf && DrawsShelfRow(Left!, y))
            {
                WriteRow(into, ref write, bounds.X, Left!.Foot, y, color);
            }

            if (rightShelf && DrawsShelfRow(Right!, y))
            {
                WriteRow(into, ref write, Right!.Foot, bounds.Right, y, color);
            }
        }
    }

    private void WriteStraight(in Box bounds, Span<MeshVertex> into, in RenderColor color)
    {
        var write = 0;
        var first = FirstVertical(bounds);
        var columns = Candidates(first, LastVertical(bounds));
        for (var i = 0; i < columns; i++)
        {
            var column = first + ((long)i * _cellSize);
            if (!DrawsColumn(column))
            {
                continue;
            }

            var x = (float)Math.Round(ToScreen(column));
            WriteColumn(into, ref write, (x, bounds.Y), (x, bounds.Bottom), color);
        }

        var firstRow = FirstHorizontal(bounds);
        var rows = Candidates(firstRow, LastHorizontal(bounds));
        for (var i = 0; i < rows; i++)
        {
            var row = firstRow + ((long)i * _cellSize);
            if (!DrawsRow(row))
            {
                continue;
            }

            var y = Math.Round(ToScreenY(row));
            WriteSegment(into, ref write, bounds.X, (float)y, bounds.Right, (float)y, color);
        }
    }

    private int Candidates(long first, long last) => last < first ? 0 : (int)((last - first) / _cellSize) + 1;

    private bool DrawsColumn(long canvasX)
    {
        if (_minSpacing <= 0)
        {
            return true;
        }

        var warp = Left is { IsIdentity: false } left && left.ContainsCanvas(canvasX) ? left
            : Right is { IsIdentity: false } right && right.ContainsCanvas(canvasX) ? right
            : null;
        return warp is null || OnStride(canvasX, warp.ScaleAt(canvasX));
    }

    private bool DrawsRow(long canvasY)
    {
        if (_minSpacing <= 0)
        {
            return true;
        }

        return EndAt(canvasY) is not { IsIdentity: false } end || OnStride(canvasY, end.ScaleAt(canvasY));
    }

    private bool DrawsShelfRow(CanvasWarp side, long canvasY) => _minSpacing <= 0 || OnStride(canvasY, side.EdgeScale);

    private bool OnStride(long canvas, double scale)
    {
        var stride = 1L;
        while (scale * _cellSize * stride < _minSpacing && stride < 1L << 20)
        {
            stride <<= 1;
        }

        return canvas / _cellSize % stride == 0;
    }

    private static int ShelfSegment(CanvasWarp? warp, in Box bounds)
    {
        if (warp is not { IsIdentity: false, Terrace: true } shelf)
        {
            return 0;
        }

        return shelf.Direction < 0 ? (shelf.Foot > bounds.X ? 1 : 0) : (shelf.Foot < bounds.Right ? 1 : 0);
    }

    private static int ZoneSegments(CanvasWarp? warp, int boundsStart, int boundsEnd)
    {
        if (warp is not { IsIdentity: false } zone || (zone.Slope == 0 && !zone.Terrace))
        {
            return 0;
        }

        var start = Math.Max(boundsStart, Math.Min(zone.Seam, zone.ScreenEdge));
        var end = Math.Min(boundsEnd, Math.Max(zone.Seam, zone.ScreenEdge));
        return end <= start ? 0 : (end - start + SegmentWidth - 1) / SegmentWidth;
    }

    private static int CornerSegments(CanvasWarp? warp) =>
        warp is { IsIdentity: false } zone
            ? Math.Max(1, ((int)Math.Ceiling(zone.ZoneWidth * CornerDepth(zone)) + SegmentWidth - 1) / SegmentWidth)
            : 0;

    private static double CornerDepth(CanvasWarp zone) =>
        zone.Terrace && zone.ZoneWidth > 0 ? (zone.ZoneWidth + (2.0 * zone.ShelfWidth)) / zone.ZoneWidth : 1.0;

    private static double TerraceFan(CanvasWarp? low, CanvasWarp? high)
    {
        var fan = 1.0;
        if (low is { IsIdentity: false, Terrace: true })
        {
            fan = Math.Min(fan, low.MinFan);
        }

        if (high is { IsIdentity: false, Terrace: true })
        {
            fan = Math.Min(fan, high.MinFan);
        }

        return fan;
    }

    private static double TerraceCenter(CanvasWarp? low, CanvasWarp? high) =>
        low is { IsIdentity: false, Terrace: true } ? low.Center : high?.Center ?? 0.0;

    private double ReachY(double screenY)
    {
        var canvasY = ToCanvasY(screenY);
        var fan = TerraceFan(Left, Right);
        if (fan >= 1.0 || Separable)
        {
            return canvasY;
        }

        var center = TerraceCenter(Left, Right);
        if ((screenY < center ? Top : Bottom) is { IsIdentity: false })
        {
            return canvasY;
        }

        var reach = center + ((screenY - center) / fan);
        return screenY < center ? Math.Min(canvasY, reach) : Math.Max(canvasY, reach);
    }

    private double ReachX(double screenX)
    {
        var canvasX = ToCanvas(screenX);
        var fan = TerraceFan(Top, Bottom);
        if (fan >= 1.0 || Separable)
        {
            return canvasX;
        }

        var center = TerraceCenter(Top, Bottom);
        if ((screenX < center ? Left : Right) is { IsIdentity: false })
        {
            return canvasX;
        }

        var reach = center + ((screenX - center) / fan);
        return screenX < center ? Math.Min(canvasX, reach) : Math.Max(canvasX, reach);
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
        if (end.Terrace)
        {
            depth = CornerDepth(end);
        }

        var crease = end.Terrace ? -1 : CreaseStep(end, across, reach, depth, segments);
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
        if (side.Terrace)
        {
            depth = CornerDepth(side);
        }

        var crease = side.Terrace ? -1 : CreaseStep(side, across, reach, depth, segments);
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

    private void WriteColumn(Span<MeshVertex> into, ref int write, (float X, float Y) from, (float X, float Y) to, in RenderColor color)
    {
        if (Terraced && Math.Abs(to.X - from.X) > Math.Abs(to.Y - from.Y))
        {
            MeshGrid.WriteCell(
                into.Slice(write, 6), 0, 0, 0, 0,
                (from.X, from.Y), (to.X, to.Y), (to.X, to.Y + 1), (from.X, from.Y + 1), color);
            write += 6;
            return;
        }

        MeshGrid.WriteCell(
            into.Slice(write, 6), 0, 0, 0, 0,
            (from.X, from.Y), (from.X + 1, from.Y), (to.X + 1, to.Y), (to.X, to.Y), color);
        write += 6;
    }

    private void WriteRow(Span<MeshVertex> into, ref int write, (double X, double Y) from, (double X, double Y) to, in RenderColor color) =>
        WriteSegment(into, ref write, (float)from.X, (float)from.Y, (float)to.X, (float)to.Y, color);

    private void WriteRow(Span<MeshVertex> into, ref int write, int x0, int x1, long y, in RenderColor color) =>
        WriteSegment(into, ref write, x0, (float)RowY(x0, y), x1, (float)RowY(x1, y), color);

    private void WriteSegment(Span<MeshVertex> into, ref int write, float x0, float y0, float x1, float y1, in RenderColor color)
    {
        if (Terraced && Math.Abs(y1 - y0) > Math.Abs(x1 - x0))
        {
            MeshGrid.WriteCell(
                into.Slice(write, 6), 0, 0, 0, 0,
                (x0, y0), (x0 + 1, y0), (x1 + 1, y1), (x1, y1), color);
            write += 6;
            return;
        }

        MeshGrid.WriteCell(
            into.Slice(write, 6), 0, 0, 0, 0,
            (x0, y0), (x1, y1), (x1, y1 + 1), (x0, y0 + 1), color);
        write += 6;
    }

    private bool Terraced =>
        Left is { IsIdentity: false, Terrace: true } || Right is { IsIdentity: false, Terrace: true } ||
        Top is { IsIdentity: false, Terrace: true } || Bottom is { IsIdentity: false, Terrace: true };

    private long FirstHorizontal(in Box bounds) => Ceiling((long)Math.Ceiling(ReachY(bounds.Y)), _cellSize);

    private long LastHorizontal(in Box bounds) => Floor((long)Math.Floor(ReachY(bounds.Bottom - 1)), _cellSize);

    private long FirstVertical(in Box bounds) => Ceiling((long)Math.Ceiling(ReachX(bounds.X)), _cellSize);

    private long LastVertical(in Box bounds) => Floor((long)Math.Floor(ReachX(bounds.Right - 1)), _cellSize);

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
