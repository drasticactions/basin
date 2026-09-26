using Basin.Scene;

namespace Basin.Effects;

public sealed class CanvasWarpTransform : IInvertibleMeshTransform
{
    public const double RimRadius = 0.5;

    private const int CornerIterations = 40;

    private const double FootTolerance = 1.0;

    private static readonly RenderColor White = new(1f, 1f, 1f, 1f);

    private int _cellSize = 16;
    private double _maxFan = double.PositiveInfinity;
    private double _maxColumnFan = double.PositiveInfinity;
    private double _cornerRadius = 1.0;
    private double _preScale = 1.0;
    private double _stretchX = 1.0;
    private double _stretchY = 1.0;
    private double _viewScale = 1.0;

    public CanvasWarp? Left { get; set; }

    public CanvasWarp? Right { get; set; }

    public CanvasWarp? Top { get; set; }

    public CanvasWarp? Bottom { get; set; }

    public int SceneX { get; set; }

    public int SceneY { get; set; }

    public double PreScale
    {
        get => _preScale;
        set => _preScale = value > 0 && double.IsFinite(value) ? Math.Min(value, 1.0) : 1.0;
    }

    public double PreStretchX
    {
        get => _stretchX;
        set => _stretchX = value > 0 && double.IsFinite(value) ? Math.Min(value, 1.0) : 1.0;
    }

    public double PreStretchY
    {
        get => _stretchY;
        set => _stretchY = value > 0 && double.IsFinite(value) ? Math.Min(value, 1.0) : 1.0;
    }

    public bool Separable { get; set; }

    public double ViewScale
    {
        get => _viewScale;
        set => _viewScale = value > 0 && double.IsFinite(value) ? value : 1.0;
    }

    public double ViewCenterX { get; set; }

    public double ViewCenterY { get; set; }

    public double PreAnchorX { get; set; }

    public double PreAnchorY { get; set; }

    public double MaxFan
    {
        get => _maxFan;
        set => _maxFan = Math.Max(1.0, value);
    }

    public double MaxColumnFan
    {
        get => _maxColumnFan;
        set => _maxColumnFan = Math.Max(1.0, value);
    }

    public double CornerRadius
    {
        get => _cornerRadius;
        set => _cornerRadius = Math.Clamp(value, 0.0, 1.0);
    }

    public bool CornerTaper { get; set; }

    public double RimDiagonal
    {
        get
        {
            var radius = RadiusAt(1.0);
            return (1.0 - radius) + (radius / Math.Sqrt(2.0));
        }
    }

    public double CornerReach(double across)
    {
        var radius = RadiusAt(1.0);
        var straight = 1.0 - radius;
        if (across <= straight)
        {
            return 1.0;
        }

        var offset = across - straight;
        return straight + Math.Sqrt(Math.Max(0.0, (radius * radius) - (offset * offset)));
    }

    private double RadiusAt(double level) =>
        CornerTaper ? _cornerRadius * (1.0 - Math.Clamp(level, 0.0, 1.0)) : _cornerRadius;

    public int CellSize
    {
        get => _cellSize;
        set => _cellSize = Math.Max(1, value);
    }

    public double ToScreen(double canvasX) => SideAt(canvasX) is { } side ? side.ToScreen(canvasX) : canvasX;

    public double ToCanvas(double screenX) => SideAtScreen(screenX) is { } side ? side.ToCanvas(screenX) : screenX;

    public double FanAt(double canvasX) =>
        SideAt(canvasX) is { } side ? RowFan(side, side.FanAt(canvasX), side.DepthAt(canvasX)) : 1.0;

    public double FanAtScreen(double screenX) =>
        SideAtScreen(screenX) is { } side ? RowFan(side, side.FanAtScreen(screenX), side.DepthAtScreen(screenX)) : 1.0;

    public double ToScreenY(double canvasX, double canvasY) =>
        SideAt(canvasX) is { } side
            ? side.Center + ((canvasY - side.Center) * RowFan(side, side.FanAt(canvasX), side.DepthAt(canvasX)))
            : canvasY;

    public double ToCanvasY(double screenX, double screenY) =>
        SideAtScreen(screenX) is { } side
            ? side.Center + ((screenY - side.Center) / RowFan(side, side.FanAtScreen(screenX), side.DepthAtScreen(screenX)))
            : screenY;

    public (double X, double Y) ToScreenPoint(double canvasX, double canvasY)
    {
        var (x, y) = Warp(PreX(canvasX), PreY(canvasY));
        return (ZoomX(x), ZoomY(y));
    }

    public (double X, double Y) ToCanvasPoint(double screenX, double screenY)
    {
        var (x, y) = Unwarp(UnzoomX(screenX), UnzoomY(screenY));
        return (UnPreX(x), UnPreY(y));
    }

    public (double X, double Y) FromWarpPoint(double warpX, double warpY)
    {
        var (x, y) = Unwarp(warpX, warpY);
        return (UnPreX(x), UnPreY(y));
    }

    public (double X, double Y) Zoom(double warpX, double warpY) => (ZoomX(warpX), ZoomY(warpY));

    public (double X, double Y) Unzoom(double screenX, double screenY) => (UnzoomX(screenX), UnzoomY(screenY));

    public (double X, double Y) LocalScale(double canvasX, double canvasY) =>
        (Math.Min(Left?.ScaleAt(canvasX) ?? 1.0, Right?.ScaleAt(canvasX) ?? 1.0),
         Math.Min(Top?.ScaleAt(canvasY) ?? 1.0, Bottom?.ScaleAt(canvasY) ?? 1.0));

    private double FactorX => _preScale * _stretchX;

    private double FactorY => _preScale * _stretchY;

    private double PreX(double x) => FactorX == 1.0 ? x : PreAnchorX + ((x - PreAnchorX) * FactorX);

    private double PreY(double y) => FactorY == 1.0 ? y : PreAnchorY + ((y - PreAnchorY) * FactorY);

    private double UnPreX(double x) => FactorX == 1.0 ? x : PreAnchorX + ((x - PreAnchorX) / FactorX);

    private double UnPreY(double y) => FactorY == 1.0 ? y : PreAnchorY + ((y - PreAnchorY) / FactorY);

    private double ZoomX(double x) => _viewScale == 1.0 ? x : ViewCenterX + ((x - ViewCenterX) * _viewScale);

    private double ZoomY(double y) => _viewScale == 1.0 ? y : ViewCenterY + ((y - ViewCenterY) * _viewScale);

    private double UnzoomX(double x) => _viewScale == 1.0 ? x : ViewCenterX + ((x - ViewCenterX) / _viewScale);

    private double UnzoomY(double y) => _viewScale == 1.0 ? y : ViewCenterY + ((y - ViewCenterY) / _viewScale);

    private RenderTransform Zoomed(in RenderTransform placement)
    {
        if (_viewScale == 1.0)
        {
            return placement;
        }

        var z = _viewScale;
        return new RenderTransform(
            placement.M11 * z, placement.M12 * z, (placement.M13 * z) + (ViewCenterX * (1.0 - z)),
            placement.M21 * z, placement.M22 * z, (placement.M23 * z) + (ViewCenterY * (1.0 - z)),
            0, 0, 1);
    }

    private (double X, double Y) Warp(double canvasX, double canvasY)
    {
        if (Separable)
        {
            return (
                SideAt(canvasX) is { } flatSide ? flatSide.ToScreen(canvasX) : canvasX,
                EndAt(canvasY) is { } flatEnd ? flatEnd.ToScreen(canvasY) : canvasY);
        }

        var side = SideAt(canvasX);
        var end = EndAt(canvasY);
        if (end is null)
        {
            return side is null ? (canvasX, canvasY) : (side.ToScreen(canvasX), ToScreenY(canvasX, canvasY));
        }

        if (side is null)
        {
            var fan = ColumnFan(end, end.FanAt(canvasY), end.DepthAt(canvasY));
            return (end.Center + ((canvasX - end.Center) * fan), end.ToScreen(canvasY));
        }

        var ux = side.Direction * (canvasX - side.Seam) / side.Extension;
        var uy = end.Direction * (canvasY - end.Seam) / end.Extension;
        var level = CornerLevel(ux, uy);
        var ring = Ring(side, end, level);
        var radius = RadiusAt(level);
        var straight = 1.0 - radius;
        if (uy <= straight * ux)
        {
            var along = uy / level;
            return (ring.EdgeX, ring.EdgeY + (along * (ring.RimY - ring.EdgeY)));
        }

        if (ux <= straight * uy)
        {
            var along = ux / level;
            return (ring.RimX + (along * (ring.EdgeX - ring.RimX)), ring.RimY);
        }

        var angle = Math.Atan2(uy - (straight * level), ux - (straight * level));
        var cos = radius * (1.0 - Math.Cos(angle));
        var sin = radius * (1.0 - Math.Sin(angle));
        return (
            ring.EdgeX + (cos * (ring.RimX - ring.EdgeX)),
            ring.RimY + (sin * (ring.EdgeY - ring.RimY)));
    }

    private (double X, double Y) Unwarp(double screenX, double screenY)
    {
        if (Separable)
        {
            return (
                SideAtScreen(screenX) is { } flatSide ? flatSide.ToCanvas(screenX) : screenX,
                EndAtScreen(screenY) is { } flatEnd ? flatEnd.ToCanvas(screenY) : screenY);
        }

        var side = SideAtScreen(screenX);
        var canvasY = screenY;
        if (side is not null)
        {
            canvasY = ToCanvasY(screenX, screenY);
            if (EndAt(canvasY) is null)
            {
                return (side.ToCanvas(screenX), canvasY);
            }
        }

        var end = EndAtScreen(screenY);
        var canvasX = screenX;
        if (end is not null)
        {
            var fan = ColumnFan(end, end.FanAtScreen(screenY), end.DepthAtScreen(screenY));
            canvasX = end.Center + ((screenX - end.Center) / fan);
            if (SideAt(canvasX) is null)
            {
                return (canvasX, end.ToCanvas(screenY));
            }
        }

        side ??= SideAt(canvasX);
        end ??= EndAt(canvasY);
        if (side is null || end is null)
        {
            return (side is null ? canvasX : side.ToCanvas(screenX), end is null ? canvasY : end.ToCanvas(screenY));
        }

        return CornerToCanvas(side, end, screenX, screenY);
    }

    public bool IsIdentityFor(in Box childBounds) => _viewScale == 1.0 && IsFlatFor(childBounds);

    public bool IsFlatFor(in Box childBounds)
    {
        var x0 = PreX(SceneX + childBounds.X);
        var x1 = PreX(SceneX + childBounds.Right);
        if (Left is { } left && (left.ContainsCanvas(x0) || left.ContainsCanvas(x1)))
        {
            return false;
        }

        if (Right is { } right && (right.ContainsCanvas(x0) || right.ContainsCanvas(x1)))
        {
            return false;
        }

        var y0 = PreY(SceneY + childBounds.Y);
        var y1 = PreY(SceneY + childBounds.Bottom);
        if (Top is { } top && (top.ContainsCanvas(y0) || top.ContainsCanvas(y1)))
        {
            return false;
        }

        if (Bottom is { } bottom && (bottom.ContainsCanvas(y0) || bottom.ContainsCanvas(y1)))
        {
            return false;
        }

        return true;
    }

    public bool IsPastFeet(in Box childBounds)
    {
        var x0 = PreX(SceneX + childBounds.X);
        var x1 = PreX(SceneX + childBounds.Right);
        var y0 = PreY(SceneY + childBounds.Y);
        var y1 = PreY(SceneY + childBounds.Bottom);
        var held = false;
        return PastFoot(Left, x0, x1, ref held) && PastFoot(Right, x0, x1, ref held) &&
            PastFoot(Top, y0, y1, ref held) && PastFoot(Bottom, y0, y1, ref held) && held;
    }

    public RenderTransform ShelfPlacement(in Box childBounds) => Zoomed(WarpShelfPlacement(childBounds));

    public RenderTransform ViewPlacement()
    {
        var fx = FactorX;
        var fy = FactorY;
        return Zoomed(fx == 1.0 && fy == 1.0
            ? RenderTransform.Identity
            : new RenderTransform(
                fx, 0, PreAnchorX * (1.0 - fx),
                0, fy, PreAnchorY * (1.0 - fy),
                0, 0, 1));
    }

    private RenderTransform WarpShelfPlacement(in Box childBounds)
    {
        var x0 = PreX(SceneX + childBounds.X);
        var x1 = PreX(SceneX + childBounds.Right);
        var y0 = PreY(SceneY + childBounds.Y);
        var y1 = PreY(SceneY + childBounds.Bottom);
        var side = Holding(Left, Right, x0, x1);
        var end = Holding(Top, Bottom, y0, y1);
        var fx = FactorX;
        var fy = FactorY;
        if (Separable)
        {
            var kx = side?.EdgeScale ?? 1.0;
            var bx = side is null ? 0.0 : side.Foot - (kx * side.FarEdge);
            var ky = end?.EdgeScale ?? 1.0;
            var by = end is null ? 0.0 : end.Foot - (ky * end.FarEdge);
            return new RenderTransform(
                kx * fx, 0, bx + (kx * PreAnchorX * (1.0 - fx)),
                0, ky * fy, by + (ky * PreAnchorY * (1.0 - fy)),
                0, 0, 1);
        }

        if (side is not null && end is not null)
        {
            var innerX = side.Direction < 0 ? x1 : x0;
            var innerY = end.Direction < 0 ? y1 : y0;
            var (screenX, screenY) = Warp(innerX, innerY);
            return CanvasScale.About(
                Math.Min(side.EdgeScale, end.EdgeScale) * _preScale, UnPreX(innerX), UnPreY(innerY), screenX, screenY);
        }

        if (side is not null)
        {
            var k = side.EdgeScale;
            return new RenderTransform(
                k * fx, 0, side.Foot - (k * side.FarEdge) + (k * PreAnchorX * (1.0 - fx)),
                0, k * fy, (side.Center * (1.0 - k)) + (k * PreAnchorY * (1.0 - fy)),
                0, 0, 1);
        }

        if (end is not null)
        {
            var k = end.EdgeScale;
            return new RenderTransform(
                k * fx, 0, (end.Center * (1.0 - k)) + (k * PreAnchorX * (1.0 - fx)),
                0, k * fy, end.Foot - (k * end.FarEdge) + (k * PreAnchorY * (1.0 - fy)),
                0, 0, 1);
        }

        return fx == 1.0 && fy == 1.0
            ? RenderTransform.Identity
            : new RenderTransform(
                fx, 0, PreAnchorX * (1.0 - fx),
                0, fy, PreAnchorY * (1.0 - fy),
                0, 0, 1);
    }

    public (double X, double Y) AxisStretch(double canvasX, double canvasY)
    {
        const double step = 0.5;
        var right = Warp(canvasX + step, canvasY).X - Warp(canvasX - step, canvasY).X;
        var down = Warp(canvasX, canvasY + step).Y - Warp(canvasX, canvasY - step).Y;
        return (Math.Max(right * _viewScale / (2.0 * step), 0.01), Math.Max(down * _viewScale / (2.0 * step), 0.01));
    }

    public RenderTransform FlatPlacement()
    {
        var (localX, localY) = LocalScale(PreAnchorX, PreAnchorY);
        var (screenX, screenY) = Warp(PreAnchorX, PreAnchorY);
        return Zoomed(CanvasScale.About(Math.Min(localX, localY) * _preScale, PreAnchorX, PreAnchorY, screenX, screenY));
    }

    private static bool PastFoot(CanvasWarp? warp, double start, double end, ref bool held)
    {
        if (warp is not { IsIdentity: false } zone || !(zone.ContainsCanvas(start) || zone.ContainsCanvas(end)))
        {
            return true;
        }

        held = true;
        if (!zone.Terrace)
        {
            return false;
        }

        return zone.Direction < 0 ? end <= zone.FarEdge + FootTolerance : start >= zone.FarEdge - FootTolerance;
    }

    private static CanvasWarp? Holding(CanvasWarp? low, CanvasWarp? high, double start, double end) =>
        low is { IsIdentity: false } && (low.ContainsCanvas(start) || low.ContainsCanvas(end)) ? low
        : high is { IsIdentity: false } && (high.ContainsCanvas(start) || high.ContainsCanvas(end)) ? high
        : null;

    public Box MapBounds(in Box childBounds)
    {
        if (childBounds.IsEmpty)
        {
            return childBounds;
        }

        var x0 = PreX(SceneX + childBounds.X);
        var x1 = PreX(SceneX + childBounds.Right);
        var y0 = PreY(SceneY + childBounds.Y);
        var y1 = PreY(SceneY + childBounds.Bottom);
        var (leftSeam, rightSeam) = ColumnSpan();
        var (topSeam, bottomSeam) = RowSpan(y0, y1);
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var minY = double.PositiveInfinity;
        var maxY = double.NegativeInfinity;
        var row = y0;
        while (true)
        {
            var column = x0;
            while (true)
            {
                var (x, y) = Warp(column, row);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                if (column >= x1)
                {
                    break;
                }

                column = NextEdge(column, x1, leftSeam, rightSeam);
            }

            if (row >= y1)
            {
                break;
            }

            row = NextEdge(row, y1, topSeam, bottomSeam);
        }

        var left = (int)Math.Floor(ZoomX(minX) - SceneX);
        var right = (int)Math.Ceiling(ZoomX(maxX) - SceneX);
        var top = (int)Math.Floor(ZoomY(minY) - SceneY);
        var bottom = (int)Math.Ceiling(ZoomY(maxY) - SceneY);
        return new Box(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public int VertexCount(in Box childBounds)
    {
        if (childBounds.IsEmpty)
        {
            return 0;
        }

        var x0 = PreX(SceneX + childBounds.X);
        var x1 = PreX(SceneX + childBounds.Right);
        var y0 = PreY(SceneY + childBounds.Y);
        var y1 = PreY(SceneY + childBounds.Bottom);
        var (leftSeam, rightSeam) = ColumnSpan();
        var (topSeam, bottomSeam) = RowSpan(y0, y1);
        return Spans(x0, x1, leftSeam, rightSeam) * Spans(y0, y1, topSeam, bottomSeam) * 6;
    }

    public void WriteVertices(in Box childBounds, Span<MeshVertex> into)
    {
        if (childBounds.IsEmpty)
        {
            return;
        }

        var x0 = PreX(SceneX + childBounds.X);
        var x1 = PreX(SceneX + childBounds.Right);
        var y0 = PreY(SceneY + childBounds.Y);
        var y1 = PreY(SceneY + childBounds.Bottom);
        var (leftSeam, rightSeam) = ColumnSpan();
        var (topSeam, bottomSeam) = RowSpan(y0, y1);
        var count = 0;
        var row = y0;
        while (row < y1)
        {
            var nextRow = NextEdge(row, y1, topSeam, bottomSeam);
            var column = x0;
            while (column < x1)
            {
                var nextColumn = NextEdge(column, x1, leftSeam, rightSeam);
                Emit(column, nextColumn, row, nextRow, count, into);
                count++;
                column = nextColumn;
            }

            row = nextRow;
        }
    }

    public bool TryMapToSource(in Box childBounds, double x, double y, out double sourceX, out double sourceY)
    {
        var (canvasX, canvasY) = ToCanvasPoint(x + SceneX, y + SceneY);
        sourceX = canvasX - SceneX;
        sourceY = canvasY - SceneY;
        return sourceX >= childBounds.X && sourceX < childBounds.Right &&
            sourceY >= childBounds.Y && sourceY < childBounds.Bottom;
    }

    private CanvasWarp? SideAt(double canvasX) =>
        Left is { } left && left.ContainsCanvas(canvasX) ? left
        : Right is { } right && right.ContainsCanvas(canvasX) ? right
        : null;

    private CanvasWarp? SideAtScreen(double screenX) =>
        Left is { } left && left.ContainsScreen(screenX) ? left
        : Right is { } right && right.ContainsScreen(screenX) ? right
        : null;

    private CanvasWarp? EndAt(double canvasY) =>
        Top is { } top && top.ContainsCanvas(canvasY) ? top
        : Bottom is { } bottom && bottom.ContainsCanvas(canvasY) ? bottom
        : null;

    private CanvasWarp? EndAtScreen(double screenY) =>
        Top is { } top && top.ContainsScreen(screenY) ? top
        : Bottom is { } bottom && bottom.ContainsScreen(screenY) ? bottom
        : null;

    private double RowFan(CanvasWarp side, double fan, double depth)
    {
        fan = Math.Min(fan, _maxFan);
        if (Top is { IsIdentity: false } top && side.Center > top.Seam)
        {
            fan = Math.Min(fan, 1.0 + ((1.0 - RimRadius) * top.ZoneWidth * depth / (side.Center - top.Seam)));
        }

        if (Bottom is { IsIdentity: false } bottom && bottom.Seam > side.Center)
        {
            fan = Math.Min(fan, 1.0 + ((1.0 - RimRadius) * bottom.ZoneWidth * depth / (bottom.Seam - side.Center)));
        }

        return fan;
    }

    private double ColumnFan(CanvasWarp end, double fan, double depth)
    {
        fan = Math.Min(fan, _maxColumnFan);
        if (Left is { IsIdentity: false } left && end.Center > left.Seam)
        {
            fan = Math.Min(fan, 1.0 + ((1.0 - RimRadius) * left.ZoneWidth * depth / (end.Center - left.Seam)));
        }

        if (Right is { IsIdentity: false } right && right.Seam > end.Center)
        {
            fan = Math.Min(fan, 1.0 + ((1.0 - RimRadius) * right.ZoneWidth * depth / (right.Seam - end.Center)));
        }

        return fan;
    }

    private (double EdgeX, double EdgeY, double RimX, double RimY) Ring(CanvasWarp side, CanvasWarp end, double depth)
    {
        var sideCanvas = side.Seam + (side.Direction * depth * side.Extension);
        var edgeX = side.ToScreen(sideCanvas);
        var edgeY = side.Center + ((end.Seam - side.Center) * RowFan(side, side.FanAt(sideCanvas), side.DepthAt(sideCanvas)));
        var endCanvas = end.Seam + (end.Direction * depth * end.Extension);
        var rimY = end.ToScreen(endCanvas);
        var rimX = end.Center + ((side.Seam - end.Center) * ColumnFan(end, end.FanAt(endCanvas), end.DepthAt(endCanvas)));
        return (edgeX, edgeY, rimX, rimY);
    }

    private double CornerLevel(double ux, double uy)
    {
        if (!CornerTaper)
        {
            return FixedLevel(ux, uy, _cornerRadius);
        }

        var high = Math.Max(ux, uy);
        if (high >= 1.0 || _cornerRadius <= 0)
        {
            return high;
        }

        var low = Math.Min(ux, uy);
        var inner = high;
        var outer = high * Math.Sqrt(2.0);
        for (var i = 0; i < CornerIterations; i++)
        {
            var middle = 0.5 * (inner + outer);
            if (Beyond(high, low, middle))
            {
                inner = middle;
            }
            else
            {
                outer = middle;
            }
        }

        return outer;
    }

    private bool Beyond(double high, double low, double level)
    {
        if (high > level)
        {
            return true;
        }

        var radius = RadiusAt(level);
        var straight = 1.0 - radius;
        if (low <= straight * level)
        {
            return false;
        }

        var dx = high - (straight * level);
        var dy = low - (straight * level);
        return (dx * dx) + (dy * dy) > radius * level * radius * level;
    }

    private static double FixedLevel(double ux, double uy, double radius)
    {
        var high = Math.Max(ux, uy);
        var low = Math.Min(ux, uy);
        var straight = 1.0 - radius;
        if (low <= straight * high)
        {
            return high;
        }

        if (straight <= 0)
        {
            return Math.Sqrt((ux * ux) + (uy * uy));
        }

        var sum = high + low;
        var squares = (high * high) + (low * low);
        var a = (2.0 * straight * straight) - (radius * radius);
        if (Math.Abs(a) < 1e-12)
        {
            return squares / (2.0 * straight * sum);
        }

        var root = Math.Sqrt(Math.Max(0.0, (straight * straight * sum * sum) - (a * squares)));
        var first = ((straight * sum) - root) / a;
        var second = ((straight * sum) + root) / a;
        return OnFillet(first, high, low, straight) ? first : second;
    }

    private static bool OnFillet(double level, double high, double low, double straight) =>
        level > 0 && level >= high - 1e-9 && straight * level <= low + 1e-9;

    private (double X, double Y) CornerToCanvas(CanvasWarp side, CanvasWarp end, double screenX, double screenY)
    {
        double depth;
        if (_cornerRadius <= 0)
        {
            var across = side.Direction * (side.ToCanvas(screenX) - side.Seam) / side.Extension;
            var along = end.Direction * (end.ToCanvas(screenY) - end.Seam) / end.Extension;
            depth = Math.Max(0.0, Math.Max(across, along));
        }
        else
        {
            var low = 0.0;
            var high = 1.0;
            while (high < 64.0 && Outside(side, end, screenX, screenY, high) > 0)
            {
                low = high;
                high *= 2.0;
            }

            for (var i = 0; i < CornerIterations; i++)
            {
                var middle = 0.5 * (low + high);
                if (Outside(side, end, screenX, screenY, middle) > 0)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            depth = high;
        }

        var ring = Ring(side, end, depth);
        var (toEdge, toRim) = Local(ring, screenX, screenY);
        var radius = RadiusAt(depth);
        var straight = 1.0 - radius;
        double ux;
        double uy;
        if (toRim <= toEdge && toEdge >= radius)
        {
            ux = depth;
            uy = (1.0 - toEdge) * depth;
        }
        else if (toRim >= radius)
        {
            ux = (1.0 - toRim) * depth;
            uy = depth;
        }
        else
        {
            var angle = Math.Atan2(1.0 - (toEdge / radius), 1.0 - (toRim / radius));
            ux = depth * (straight + (radius * Math.Cos(angle)));
            uy = depth * (straight + (radius * Math.Sin(angle)));
        }

        return (side.Seam + (side.Direction * ux * side.Extension), end.Seam + (end.Direction * uy * end.Extension));
    }

    private static (double ToEdge, double ToRim) Local(in (double EdgeX, double EdgeY, double RimX, double RimY) ring, double screenX, double screenY)
    {
        var spanY = ring.EdgeY - ring.RimY;
        var spanX = ring.RimX - ring.EdgeX;
        var toEdge = spanY != 0 ? Math.Clamp((screenY - ring.RimY) / spanY, 0.0, 1.0) : 0.0;
        var toRim = spanX != 0 ? Math.Clamp((screenX - ring.EdgeX) / spanX, 0.0, 1.0) : 0.0;
        return (toEdge, toRim);
    }

    private double Outside(CanvasWarp side, CanvasWarp end, double screenX, double screenY, double depth)
    {
        var ring = Ring(side, end, depth);
        var spanY = ring.EdgeY - ring.RimY;
        var spanX = ring.RimX - ring.EdgeX;
        if (spanX == 0 || spanY == 0)
        {
            return double.PositiveInfinity;
        }

        var toEdge = (screenY - ring.RimY) / spanY;
        var toRim = (screenX - ring.EdgeX) / spanX;
        if (toEdge < 0 || toRim < 0)
        {
            return 1.0;
        }

        var radius = RadiusAt(depth);
        if (toEdge >= radius || toRim >= radius)
        {
            return -1.0;
        }

        var dx = toRim - radius;
        var dy = toEdge - radius;
        return (dx * dx) + (dy * dy) - (radius * radius);
    }

    private (int Start, int End) ColumnSpan()
    {
        var leftSeam = Left is { IsIdentity: false } left ? left.Seam : int.MinValue;
        var rightSeam = Right is { IsIdentity: false } right ? right.Seam : int.MaxValue;
        return (leftSeam, Math.Max(leftSeam, rightSeam));
    }

    private (double Start, double End) RowSpan(double y0, double y1)
    {
        var start = Top is { IsIdentity: false } top ? Math.Clamp(top.Seam, y0, y1) : y0;
        var end = Bottom is { IsIdentity: false } bottom ? Math.Clamp(bottom.Seam, y0, y1) : y1;
        return (start, Math.Max(start, end));
    }

    private double NextEdge(double cursor, double end, double flatStart, double flatEnd)
    {
        if (cursor < flatStart)
        {
            return Math.Min(end, Math.Min(flatStart, cursor + _cellSize));
        }

        if (cursor < flatEnd)
        {
            return Math.Min(end, flatEnd);
        }

        return Math.Min(end, cursor + _cellSize);
    }

    private int Spans(double start, double end, double flatStart, double flatEnd)
    {
        var count = 0;
        var cursor = start;
        while (cursor < end)
        {
            cursor = NextEdge(cursor, end, flatStart, flatEnd);
            count++;
        }

        return count;
    }

    private void Emit(double canvasLeft, double canvasRight, double canvasTop, double canvasBottom, int index, Span<MeshVertex> into)
    {
        var (topLeftX, topLeftY) = Warp(canvasLeft, canvasTop);
        var (topRightX, topRightY) = Warp(canvasRight, canvasTop);
        var (bottomRightX, bottomRightY) = Warp(canvasRight, canvasBottom);
        var (bottomLeftX, bottomLeftY) = Warp(canvasLeft, canvasBottom);
        MeshGrid.WriteCell(
            into.Slice(index * 6, 6),
            (float)(UnPreX(canvasLeft) - SceneX),
            (float)(UnPreY(canvasTop) - SceneY),
            (float)(UnPreX(canvasRight) - SceneX),
            (float)(UnPreY(canvasBottom) - SceneY),
            ((float)(ZoomX(topLeftX) - SceneX), (float)(ZoomY(topLeftY) - SceneY)),
            ((float)(ZoomX(topRightX) - SceneX), (float)(ZoomY(topRightY) - SceneY)),
            ((float)(ZoomX(bottomRightX) - SceneX), (float)(ZoomY(bottomRightY) - SceneY)),
            ((float)(ZoomX(bottomLeftX) - SceneX), (float)(ZoomY(bottomLeftY) - SceneY)),
            White);
    }
}
