using Basin.Scene;

namespace Basin.Effects;

public sealed class CanvasStepSurfaceSource : IMeshSource
{
    public const int ForeshortenRows = 8;

    public const double MinTextureScale = 0.25;

    public const double MaxTextureScale = 8.0;

    private const double DepthShade = 0.35;

    private const double FloorShade = 0.35;

    private const double MinTileScreen = 2.0;

    private const double MaxWallTiles = 4096;

    private static readonly CanvasStepSides[] Order =
        [CanvasStepSides.Top, CanvasStepSides.Left, CanvasStepSides.Right, CanvasStepSides.Bottom];

    private double _wallShade = 0.25;
    private double _textureScale = 1.0;
    private int _textureWidth;
    private int _textureHeight;

    public CanvasStepSurfaceSource(CanvasStepSurface surface)
    {
        Surface = surface;
    }

    public CanvasStepSurface Surface { get; }

    public CanvasStepMap? Map { get; set; }

    public int TextureWidth
    {
        get => _textureWidth;
        set => _textureWidth = Math.Max(0, value);
    }

    public int TextureHeight
    {
        get => _textureHeight;
        set => _textureHeight = Math.Max(0, value);
    }

    public double TextureScale
    {
        get => _textureScale;
        set => _textureScale = double.IsFinite(value) ? Math.Clamp(value, MinTextureScale, MaxTextureScale) : 1.0;
    }

    public RenderColor WallColor { get; set; } = new(0x26 / 255f, 0x2a / 255f, 0x3a / 255f, 1f);

    public double WallShade
    {
        get => _wallShade;
        set => _wallShade = Math.Clamp(value, 0.0, 1.0);
    }

    public RenderColor FloorColor { get; set; } = new(0x3a / 255f, 0x3d / 255f, 0x44 / 255f, 1f);

    public bool Textured => _textureWidth > 0 && _textureHeight > 0;

    public RenderColor WallColorOf(CanvasStepSides side) => side switch
    {
        CanvasStepSides.Top => Lighten(WallColor, _wallShade),
        CanvasStepSides.Left => Lighten(WallColor, _wallShade * 0.5),
        CanvasStepSides.Right => Darken(WallColor, _wallShade * 0.5),
        CanvasStepSides.Bottom => Darken(WallColor, _wallShade),
        _ => WallColor,
    };

    public RenderColor BaseColorOf(CanvasStepSides side) => Darken(WallColorOf(side), DepthShade);

    public static double Foreshorten(double t, double zoom, double shelfScale)
    {
        var denominator = shelfScale + (t * (zoom - shelfScale));
        return denominator <= 0 ? t : t * zoom / denominator;
    }

    public static double Unforeshorten(double a, double zoom, double shelfScale)
    {
        var denominator = zoom - (a * (zoom - shelfScale));
        return denominator <= 0 ? a : a * shelfScale / denominator;
    }

    public static double MaterialDepth(double width, double zoom, double shelfScale)
    {
        var middle = 2.0 * zoom * shelfScale / (zoom + shelfScale);
        return middle > 0 ? width / middle : width;
    }

    public int VertexCount(in Box bounds) => Walk(bounds, [], write: false);

    public void WriteVertices(in Box bounds, Span<MeshVertex> into) => _ = Walk(bounds, into, write: true);

    private int Walk(in Box bounds, Span<MeshVertex> into, bool write)
    {
        if (Map is not { } map || bounds.IsEmpty || map.IsIdentity)
        {
            return 0;
        }

        var count = 0;
        if (Surface == CanvasStepSurface.Floor)
        {
            if (Textured)
            {
                Floor(map, bounds, into, ref count, write);
            }

            return count;
        }

        foreach (var side in Order)
        {
            if (map.WallWidth(side) > 0)
            {
                if (Textured)
                {
                    TexturedWall(map, side, into, ref count, write);
                }
                else
                {
                    FlatWall(map, side, into, ref count, write);
                }
            }
        }

        return count;
    }

    public static void Corners(
        CanvasStepMap map, CanvasStepSides side,
        out (double X, double Y) inner0, out (double X, double Y) inner1, out (double X, double Y) outer0, out (double X, double Y) outer1)
    {
        var d = map.Outline;
        var b = map.Inner;
        switch (side)
        {
            case CanvasStepSides.Left:
                inner0 = (d.X, d.Y);
                inner1 = (d.X, d.Bottom);
                outer0 = (b.X, b.Y);
                outer1 = (b.X, b.Bottom);
                break;
            case CanvasStepSides.Right:
                inner0 = (d.Right, d.Y);
                inner1 = (d.Right, d.Bottom);
                outer0 = (b.Right, b.Y);
                outer1 = (b.Right, b.Bottom);
                break;
            case CanvasStepSides.Top:
                inner0 = (d.X, d.Y);
                inner1 = (d.Right, d.Y);
                outer0 = (b.X, b.Y);
                outer1 = (b.Right, b.Y);
                break;
            default:
                inner0 = (d.X, d.Bottom);
                inner1 = (d.Right, d.Bottom);
                outer0 = (b.X, b.Bottom);
                outer1 = (b.Right, b.Bottom);
                break;
        }
    }

    private void FlatWall(CanvasStepMap map, CanvasStepSides side, Span<MeshVertex> into, ref int count, bool write)
    {
        if (write)
        {
            Corners(map, side, out var inner0, out var inner1, out var outer0, out var outer1);
            var lit = WallColorOf(side);
            var foot = BaseColorOf(side);
            var slice = into.Slice(count, 6);
            slice[0] = Vertex(inner0, 0, 0, lit);
            slice[1] = Vertex(inner1, 0, 0, lit);
            slice[2] = Vertex(outer1, 0, 0, foot);
            slice[3] = Vertex(inner0, 0, 0, lit);
            slice[4] = Vertex(outer1, 0, 0, foot);
            slice[5] = Vertex(outer0, 0, 0, foot);
        }

        count += 6;
    }

    private void TexturedWall(CanvasStepMap map, CanvasStepSides side, Span<MeshVertex> into, ref int count, bool write)
    {
        Corners(map, side, out var inner0, out var inner1, out var outer0, out var outer1);
        var horizontal = side is CanvasStepSides.Top or CanvasStepSides.Bottom;
        var center = horizontal ? map.CenterX : map.CenterY;
        var start = horizontal ? inner0.X : inner0.Y;
        var end = horizontal ? inner1.X : inner1.Y;
        var zoom = map.Zoom;
        var shelf = map.ShelfScale;
        var tileU = _textureWidth * _textureScale;
        var tileV = _textureHeight * _textureScale;
        var u0 = (center + ((start - center) / zoom)) / tileU;
        var u1 = (center + ((end - center) / zoom)) / tileU;
        var depth = MaterialDepth(map.WallWidth(side), zoom, shelf) / tileV;
        if (!(u1 > u0) || !(depth > 0) || !double.IsFinite(u0) || !double.IsFinite(u1) ||
            (end - start) / (u1 - u0) < MinTileScreen || depth > MaxWallTiles)
        {
            return;
        }

        var lit = WallColorOf(side);
        var foot = BaseColorOf(side);
        var columnCount = (int)(Math.Ceiling(u1) - Math.Floor(u0));
        var crossings = (int)Math.Ceiling(depth) - 1;
        var rowCount = ForeshortenRows + crossings;
        if (!write)
        {
            count += columnCount * rowCount * 6;
            return;
        }

        var s0 = 0.0;
        var column = Math.Floor(u0);
        for (var index = 0; index < columnCount; index++)
        {
            var s1 = index == columnCount - 1 ? 1.0 : Math.Min(1.0, ((column + 1) - u0) / (u1 - u0));
            var uStart = u0 + (s0 * (u1 - u0));
            var uEnd = u0 + (s1 * (u1 - u0));
            var left = (float)Math.Clamp((uStart - column) * _textureWidth, 0, _textureWidth);
            var right = (float)Math.Clamp((uEnd - column) * _textureWidth, 0, _textureWidth);
            var top0 = Lerp(inner0, inner1, s0);
            var top1 = Lerp(inner0, inner1, s1);
            var base0 = Lerp(outer0, outer1, s0);
            var base1 = Lerp(outer0, outer1, s1);

            var t0 = 0.0;
            var uniform = 1;
            var crossing = 1;
            for (var row = 0; row < rowCount; row++)
            {
                var nextUniform = uniform <= ForeshortenRows ? uniform / (double)ForeshortenRows : double.PositiveInfinity;
                var nextCrossing = crossing <= crossings ? Unforeshorten(crossing / depth, zoom, shelf) : double.PositiveInfinity;
                double t1;
                if (nextCrossing < nextUniform)
                {
                    t1 = nextCrossing;
                    crossing++;
                }
                else
                {
                    t1 = nextUniform;
                    uniform++;
                    if (nextCrossing == nextUniform)
                    {
                        crossing++;
                    }
                }

                t1 = Math.Min(1.0, t1);
                var v0 = Foreshorten(t0, zoom, shelf) * depth;
                var v1 = Foreshorten(t1, zoom, shelf) * depth;
                var tile = Math.Floor((v0 + v1) / 2.0);
                var top = (float)Math.Clamp((v0 - tile) * _textureHeight, 0, _textureHeight);
                var bottom = (float)Math.Clamp((v1 - tile) * _textureHeight, 0, _textureHeight);
                var colorTop = Lerp(lit, foot, t0);
                var colorBottom = Lerp(lit, foot, t1);
                var slice = into.Slice(count, 6);
                var a = new MeshVertex((float)Lerp(top0, base0, t0).X, (float)Lerp(top0, base0, t0).Y, left, top, colorTop);
                var b = new MeshVertex((float)Lerp(top1, base1, t0).X, (float)Lerp(top1, base1, t0).Y, right, top, colorTop);
                var c = new MeshVertex((float)Lerp(top1, base1, t1).X, (float)Lerp(top1, base1, t1).Y, right, bottom, colorBottom);
                var d = new MeshVertex((float)Lerp(top0, base0, t1).X, (float)Lerp(top0, base0, t1).Y, left, bottom, colorBottom);
                slice[0] = a;
                slice[1] = b;
                slice[2] = d;
                slice[3] = b;
                slice[4] = c;
                slice[5] = d;
                count += 6;
                t0 = t1;
            }

            s0 = s1;
            column++;
        }
    }

    private void Floor(CanvasStepMap map, in Box bounds, Span<MeshVertex> into, ref int count, bool write)
    {
        var u = map.Outer;
        var b = map.Inner;
        var sides = map.Shelves;
        var left = (sides & CanvasStepSides.Left) != 0;
        var right = (sides & CanvasStepSides.Right) != 0;
        var top = (sides & CanvasStepSides.Top) != 0;
        var bottom = (sides & CanvasStepSides.Bottom) != 0;
        var shade = new FloorShading(map, left, right, top, bottom);
        if (left)
        {
            Column(map, shade, u.X, b.X, top, bottom, -1, bounds, into, ref count, write);
        }

        if (right)
        {
            Column(map, shade, b.Right, u.Right, top, bottom, 1, bounds, into, ref count, write);
        }

        var x0 = left ? b.X : u.X;
        var x1 = right ? b.Right : u.Right;
        if (top)
        {
            Cells(map, shade, Clip(Span(x0, u.Y, x1, b.Y), bounds), Diagonal.None, into, ref count, write);
        }

        if (bottom)
        {
            Cells(map, shade, Clip(Span(x0, b.Bottom, x1, u.Bottom), bounds), Diagonal.None, into, ref count, write);
        }
    }

    private void Column(
        CanvasStepMap map, in FloorShading shade, double x0, double x1, bool top, bool bottom, int direction,
        in Box bounds, Span<MeshVertex> into, ref int count, bool write)
    {
        var u = map.Outer;
        var b = map.Inner;
        var y0 = u.Y;
        var y1 = u.Bottom;
        if (top)
        {
            var corner = direction < 0 ? Diagonal.TopLeft : Diagonal.TopRight;
            Cells(map, shade, Clip(Span(x0, u.Y, x1, b.Y), bounds), corner, into, ref count, write);
            y0 = b.Y;
        }

        if (bottom)
        {
            var corner = direction < 0 ? Diagonal.BottomLeft : Diagonal.BottomRight;
            Cells(map, shade, Clip(Span(x0, b.Bottom, x1, u.Bottom), bounds), corner, into, ref count, write);
            y1 = b.Bottom;
        }

        Cells(map, shade, Clip(Span(x0, y0, x1, y1), bounds), Diagonal.None, into, ref count, write);
    }

    private void Cells(
        CanvasStepMap map, in FloorShading shade, in FBox area, Diagonal diagonal, Span<MeshVertex> into, ref int count, bool write)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        var scale = map.ShelfScale;
        var tileX = _textureWidth * _textureScale;
        var tileY = _textureHeight * _textureScale;
        if (tileX * scale < MinTileScreen || tileY * scale < MinTileScreen)
        {
            return;
        }

        var columnX = Math.Floor(CanvasOf(area.X, map.CenterX, scale) / tileX);
        var x0 = area.X;
        while (x0 < area.Right)
        {
            var x1 = Math.Min(area.Right, map.CenterX + ((((columnX + 1) * tileX) - map.CenterX) * scale));
            if (x1 <= x0)
            {
                columnX++;
                continue;
            }

            var rowY = Math.Floor(CanvasOf(area.Y, map.CenterY, scale) / tileY);
            var y0 = area.Y;
            while (y0 < area.Bottom)
            {
                var y1 = Math.Min(area.Bottom, map.CenterY + ((((rowY + 1) * tileY) - map.CenterY) * scale));
                if (y1 <= y0)
                {
                    rowY++;
                    continue;
                }

                Cell(map, shade, x0, y0, x1, y1, columnX, rowY, diagonal, into, ref count, write);
                y0 = y1;
                rowY++;
            }

            x0 = x1;
            columnX++;
        }
    }

    private void Cell(
        CanvasStepMap map, in FloorShading shade, double x0, double y0, double x1, double y1, double column, double row,
        Diagonal diagonal, Span<MeshVertex> into, ref int count, bool write)
    {
        Span<(double X, double Y)> corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)];
        if (diagonal == Diagonal.None)
        {
            Polygon(map, shade, corners, column, row, into, ref count, write);
            return;
        }

        Span<double> side = stackalloc double[4];
        var positive = false;
        var negative = false;
        for (var i = 0; i < 4; i++)
        {
            side[i] = shade.Split(diagonal, corners[i].X, corners[i].Y);
            positive |= side[i] > 0;
            negative |= side[i] < 0;
        }

        if (!positive || !negative)
        {
            Polygon(map, shade, corners, column, row, into, ref count, write);
            return;
        }

        Span<(double X, double Y)> half = stackalloc (double X, double Y)[6];
        for (var sign = 1; sign >= -1; sign -= 2)
        {
            var n = 0;
            for (var i = 0; i < 4; i++)
            {
                var j = (i + 1) % 4;
                var here = side[i] * sign;
                var next = side[j] * sign;
                if (here >= 0)
                {
                    half[n++] = corners[i];
                }

                if ((here > 0 && next < 0) || (here < 0 && next > 0))
                {
                    var t = here / (here - next);
                    half[n++] = (corners[i].X + ((corners[j].X - corners[i].X) * t), corners[i].Y + ((corners[j].Y - corners[i].Y) * t));
                }
            }

            Polygon(map, shade, half[..n], column, row, into, ref count, write);
        }
    }

    private void Polygon(
        CanvasStepMap map, in FloorShading shade, ReadOnlySpan<(double X, double Y)> points, double column, double row,
        Span<MeshVertex> into, ref int count, bool write)
    {
        if (points.Length < 3)
        {
            return;
        }

        var triangles = points.Length - 2;
        if (write)
        {
            var first = FloorVertex(map, shade, points[0], column, row);
            var previous = FloorVertex(map, shade, points[1], column, row);
            for (var i = 2; i < points.Length; i++)
            {
                var next = FloorVertex(map, shade, points[i], column, row);
                var slice = into.Slice(count + ((i - 2) * 3), 3);
                slice[0] = first;
                slice[1] = previous;
                slice[2] = next;
                previous = next;
            }
        }

        count += triangles * 3;
    }

    private MeshVertex FloorVertex(CanvasStepMap map, in FloorShading shade, (double X, double Y) point, double column, double row)
    {
        var scale = map.ShelfScale;
        var canvasX = CanvasOf(point.X, map.CenterX, scale);
        var canvasY = CanvasOf(point.Y, map.CenterY, scale);
        var sampleX = (float)Math.Clamp((canvasX / _textureScale) - (column * _textureWidth), 0, _textureWidth);
        var sampleY = (float)Math.Clamp((canvasY / _textureScale) - (row * _textureHeight), 0, _textureHeight);
        var color = Darken(FloorColor, FloorShade * shade.Depth(point.X, point.Y));
        return new MeshVertex((float)point.X, (float)point.Y, sampleX, sampleY, color);
    }

    private static double CanvasOf(double screen, double center, double scale) => center + ((screen - center) / scale);

    private static FBox Span(double left, double top, double right, double bottom) =>
        new(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));

    private static FBox Clip(in FBox area, in Box bounds) => Span(
        Math.Max(area.X, bounds.X), Math.Max(area.Y, bounds.Y), Math.Min(area.Right, bounds.Right), Math.Min(area.Bottom, bounds.Bottom));

    private static (double X, double Y) Lerp((double X, double Y) from, (double X, double Y) to, double t) =>
        (from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));

    private static RenderColor Lerp(in RenderColor from, in RenderColor to, double t)
    {
        var f = (float)t;
        return new RenderColor(
            from.R + ((to.R - from.R) * f), from.G + ((to.G - from.G) * f), from.B + ((to.B - from.B) * f), from.A + ((to.A - from.A) * f));
    }

    private static MeshVertex Vertex((double X, double Y) point, float u, float v, in RenderColor color) =>
        new((float)point.X, (float)point.Y, u, v, color);

    private static RenderColor Lighten(in RenderColor color, double amount)
    {
        var t = (float)amount;
        return new RenderColor(
            color.R + ((color.A - color.R) * t), color.G + ((color.A - color.G) * t), color.B + ((color.A - color.B) * t), color.A);
    }

    private static RenderColor Darken(in RenderColor color, double amount)
    {
        var keep = 1f - (float)amount;
        return new RenderColor(color.R * keep, color.G * keep, color.B * keep, color.A);
    }

    private enum Diagonal
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    private readonly struct FloorShading
    {
        private readonly double _baseLeft;
        private readonly double _baseRight;
        private readonly double _baseTop;
        private readonly double _baseBottom;
        private readonly double _left;
        private readonly double _right;
        private readonly double _top;
        private readonly double _bottom;

        public FloorShading(CanvasStepMap map, bool left, bool right, bool top, bool bottom)
        {
            var u = map.Outer;
            var b = map.Inner;
            _baseLeft = b.X;
            _baseRight = b.Right;
            _baseTop = b.Y;
            _baseBottom = b.Bottom;
            _left = left && b.X > u.X ? b.X - u.X : 0;
            _right = right && u.Right > b.Right ? u.Right - b.Right : 0;
            _top = top && b.Y > u.Y ? b.Y - u.Y : 0;
            _bottom = bottom && u.Bottom > b.Bottom ? u.Bottom - b.Bottom : 0;
        }

        private double Left(double x) => _left > 0 ? (_baseLeft - x) / _left : double.NegativeInfinity;

        private double Right(double x) => _right > 0 ? (x - _baseRight) / _right : double.NegativeInfinity;

        private double Top(double y) => _top > 0 ? (_baseTop - y) / _top : double.NegativeInfinity;

        private double Bottom(double y) => _bottom > 0 ? (y - _baseBottom) / _bottom : double.NegativeInfinity;

        public double Depth(double x, double y) =>
            Math.Clamp(Math.Max(Math.Max(Left(x), Right(x)), Math.Max(Top(y), Bottom(y))), 0.0, 1.0);

        public double Split(Diagonal diagonal, double x, double y)
        {
            var value = diagonal switch
            {
                Diagonal.TopLeft => Left(x) - Top(y),
                Diagonal.TopRight => Right(x) - Top(y),
                Diagonal.BottomLeft => Left(x) - Bottom(y),
                Diagonal.BottomRight => Right(x) - Bottom(y),
                _ => 0.0,
            };
            return double.IsFinite(value) && Math.Abs(value) > 1e-9 ? value : 0.0;
        }
    }
}
