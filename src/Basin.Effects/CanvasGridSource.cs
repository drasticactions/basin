using Basin.Scene;

namespace Basin.Effects;

public sealed class CanvasGridSource : IMeshSource
{
    private const int SegmentWidth = 8;

    private int _cellSize = 64;
    private float _alpha = 1f;

    public CanvasWarp? Left { get; set; }

    public CanvasWarp? Right { get; set; }

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

    public double ToScreen(double canvasX)
    {
        if (Left is { } left && left.ContainsCanvas(canvasX))
        {
            return left.ToScreen(canvasX);
        }

        if (Right is { } right && right.ContainsCanvas(canvasX))
        {
            return right.ToScreen(canvasX);
        }

        return canvasX;
    }

    public double ToCanvas(double screenX)
    {
        if (Left is { } left && left.ContainsScreen(screenX))
        {
            return left.ToCanvas(screenX);
        }

        if (Right is { } right && right.ContainsScreen(screenX))
        {
            return right.ToCanvas(screenX);
        }

        return screenX;
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

        var first = Ceiling(bounds.Y, _cellSize);
        var last = Floor(bounds.Bottom - 1, _cellSize);
        return last < first ? 0 : ((last - first) / _cellSize) + 1;
    }

    public int RowSegments(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        return 1 + ZoneSegments(Left, bounds) + ZoneSegments(Right, bounds);
    }

    public int VertexCount(in Box bounds) => (VerticalLines(bounds) + (HorizontalLines(bounds) * RowSegments(bounds))) * 6;

    private static int ZoneSegments(CanvasWarp? warp, in Box bounds)
    {
        if (warp is not { IsIdentity: false } zone || zone.Slope == 0)
        {
            return 0;
        }

        var start = Math.Max(bounds.X, Math.Min(zone.Seam, zone.ScreenEdge));
        var end = Math.Min(bounds.Right, Math.Max(zone.Seam, zone.ScreenEdge));
        return end <= start ? 0 : (end - start + SegmentWidth - 1) / SegmentWidth;
    }

    private double FanAtScreen(double screenX)
    {
        if (Left is { } left && left.ContainsScreen(screenX))
        {
            return left.FanAtScreen(screenX);
        }

        if (Right is { } right && right.ContainsScreen(screenX))
        {
            return right.FanAtScreen(screenX);
        }

        return 1.0;
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
        var fan = FanAtScreen(screenX);
        if (fan == 1.0)
        {
            return y;
        }

        var center = CenterAt(screenX);
        return center + ((y - center) * fan);
    }

    public void WriteVertices(in Box bounds, Span<MeshVertex> into)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        var color = new RenderColor(Color.R * _alpha, Color.G * _alpha, Color.B * _alpha, Color.A * _alpha);
        var write = 0;
        var top = bounds.Y;
        var bottom = bounds.Bottom;
        var first = FirstVertical(bounds);
        var count = VerticalLines(bounds);
        for (var i = 0; i < count; i++)
        {
            var canvasX = first + ((long)i * _cellSize);
            var x = (float)Math.Round(ToScreen(canvasX));
            MeshGrid.WriteCell(
                into.Slice(write, 6), 0, 0, 0, 0,
                (x, top), (x + 1, top), (x + 1, bottom), (x, bottom), color);
            write += 6;
        }

        var firstRow = Ceiling(bounds.Y, _cellSize);
        var rows = HorizontalLines(bounds);
        var leftSegments = ZoneSegments(Left, bounds);
        var rightSegments = ZoneSegments(Right, bounds);
        var flatLeft = leftSegments > 0 ? Left!.Seam : bounds.X;
        var flatRight = rightSegments > 0 ? Right!.Seam : bounds.Right;
        for (var i = 0; i < rows; i++)
        {
            var y = firstRow + (i * _cellSize);
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

    private void WriteRow(Span<MeshVertex> into, ref int write, int x0, int x1, int y, in RenderColor color)
    {
        var y0 = (float)RowY(x0, y);
        var y1 = (float)RowY(x1, y);
        MeshGrid.WriteCell(
            into.Slice(write, 6), 0, 0, 0, 0,
            (x0, y0), (x1, y1), (x1, y1 + 1), (x0, y0 + 1), color);
        write += 6;
    }

    private long FirstVertical(in Box bounds) => Ceiling((long)Math.Ceiling(ToCanvas(bounds.X)), _cellSize);

    private long LastVertical(in Box bounds) => Floor((long)Math.Floor(ToCanvas(bounds.Right - 1)), _cellSize);

    private static long Ceiling(long value, int cell)
    {
        var remainder = value % cell;
        return remainder == 0 ? value : remainder > 0 ? value + (cell - remainder) : value - remainder;
    }

    private static int Ceiling(int value, int cell) => (int)Ceiling((long)value, cell);

    private static long Floor(long value, int cell)
    {
        var remainder = value % cell;
        return remainder == 0 ? value : remainder > 0 ? value - remainder : value - (cell + remainder);
    }

    private static int Floor(int value, int cell) => (int)Floor((long)value, cell);
}
