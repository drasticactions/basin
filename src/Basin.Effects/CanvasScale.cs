using Basin.Diagnostics;

namespace Basin.Effects;

public sealed class CanvasScale
{
    public const double DefaultMinScale = 0.35;

    private const int ResizeIterations = 48;

    private double _minScale = DefaultMinScale;
    private double _reach = 1.0;

    public double MinScale
    {
        get => _minScale;
        set => _minScale = Math.Clamp(value, 0.05, 1.0);
    }

    public double Reach
    {
        get => _reach;
        set => _reach = Math.Clamp(value, 1.0, 16.0);
    }

    public double AxisScale(CanvasWarp? warp, double inner)
    {
        if (warp is null || warp.IsIdentity)
        {
            return 1.0;
        }

        var u = warp.Direction * (inner - warp.Seam) / warp.Extension;
        if (u <= 0)
        {
            return 1.0;
        }

        var v = u / _reach;
        if (v >= 1.0)
        {
            return warp.EdgeScale;
        }

        return warp.ScaleAt(warp.Seam + (warp.Direction * warp.Extension * v));
    }

    public double ScaleFor(CanvasWarpTransform map, in Box canvasBox)
    {
        var (kx, _) = Axis(Across(map.Left, map.Right, canvasBox.X, canvasBox.Width), canvasBox.X, canvasBox.Width);
        var (ky, _) = Axis(Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height), canvasBox.Y, canvasBox.Height);
        return Floor(Math.Min(kx, ky));
    }

    public (double X, double Y) AnchorFor(CanvasWarpTransform map, in Box canvasBox)
    {
        var (_, x) = Axis(Across(map.Left, map.Right, canvasBox.X, canvasBox.Width), canvasBox.X, canvasBox.Width);
        var (_, y) = Axis(Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height), canvasBox.Y, canvasBox.Height);
        return (x, y);
    }

    public RenderTransform PlacementFor(CanvasWarpTransform map, in Box canvasBox)
    {
        AllocationScope.Begin();
        var (kx, anchorX) = Axis(Across(map.Left, map.Right, canvasBox.X, canvasBox.Width), canvasBox.X, canvasBox.Width);
        var (ky, anchorY) = Axis(Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height), canvasBox.Y, canvasBox.Height);
        var k = Floor(Math.Min(kx, ky));
        var (screenX, screenY) = map.ToScreenPoint(anchorX, anchorY);
        var placement = About(k, anchorX, anchorY, screenX, screenY);
        AllocationScope.End();
        return placement;
    }

    public double ScaleAtScreen(CanvasWarpTransform map, double screenX, double screenY)
    {
        var depth = Math.Max(
            Math.Max(ScreenDepth(map.Left, screenX), ScreenDepth(map.Right, screenX)),
            Math.Max(ScreenDepth(map.Top, screenY), ScreenDepth(map.Bottom, screenY)));
        if (depth <= 0)
        {
            return 1.0;
        }

        var eased = depth * depth * (3.0 - (2.0 * depth));
        return 1.0 - ((1.0 - _minScale) * eased);
    }

    public RenderTransform AnchoredPlacementFor(CanvasWarpTransform map, in Box canvasBox, double anchorX, double anchorY)
    {
        var (screenX, screenY) = map.ToScreenPoint(anchorX, anchorY);
        return HandPlacementFor(map, canvasBox, anchorX, anchorY, screenX, screenY);
    }

    public RenderTransform HandPlacementFor(
        CanvasWarpTransform map,
        in Box canvasBox,
        double anchorX,
        double anchorY,
        double screenX,
        double screenY,
        double bias = 0.0)
    {
        AllocationScope.Begin();
        var low = _minScale;
        var high = 1.0;
        if (HandResidual(map, canvasBox, anchorX, anchorY, screenX, screenY, bias, high) <= 0)
        {
            low = high;
        }
        else
        {
            for (var i = 0; i < ResizeIterations; i++)
            {
                var middle = 0.5 * (low + high);
                if (HandResidual(map, canvasBox, anchorX, anchorY, screenX, screenY, bias, middle) <= 0)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
        }

        var placement = About(low, anchorX, anchorY, screenX, screenY);
        AllocationScope.End();
        return placement;
    }

    private double HandResidual(
        CanvasWarpTransform map, in Box canvasBox, double anchorX, double anchorY, double screenX, double screenY, double bias, double k)
    {
        var left = screenX - (k * (anchorX - canvasBox.X));
        var right = screenX + (k * (canvasBox.Right - anchorX));
        var top = screenY - (k * (anchorY - canvasBox.Y));
        var bottom = screenY + (k * (canvasBox.Bottom - anchorY));
        var depth = Math.Max(
            Math.Max(ScreenDepth(map.Left, left), ScreenDepth(map.Right, right)),
            Math.Max(ScreenDepth(map.Top, top), ScreenDepth(map.Bottom, bottom)));
        var eased = depth * depth * (3.0 - (2.0 * depth));
        var wanted = Math.Clamp(1.0 - ((1.0 - _minScale) * eased) + bias, _minScale, 1.0);
        return k - wanted;
    }

    public RenderTransform DragPlacementFor(
        CanvasWarpTransform map,
        in Box canvasBox,
        double grabX,
        double grabY,
        double cursorX,
        double cursorY)
    {
        AllocationScope.Begin();
        var placement = About(ScaleFor(map, canvasBox), grabX, grabY, cursorX, cursorY);
        AllocationScope.End();
        return placement;
    }

    public static RenderTransform About(double scale, double canvasX, double canvasY, double screenX, double screenY)
    {
        if (scale == 1.0 && canvasX == screenX && canvasY == screenY)
        {
            return RenderTransform.Identity;
        }

        return new RenderTransform(
            scale, 0, screenX - (scale * canvasX),
            0, scale, screenY - (scale * canvasY),
            0, 0, 1);
    }

    public static RenderTransform Blend(in RenderTransform from, in RenderTransform to, double t)
    {
        if (t <= 0)
        {
            return from;
        }

        if (t >= 1)
        {
            return to;
        }

        var scaleX = from.M11 + ((to.M11 - from.M11) * t);
        var scaleY = from.M22 + ((to.M22 - from.M22) * t);
        var x = from.M13 + ((to.M13 - from.M13) * t);
        var y = from.M23 + ((to.M23 - from.M23) * t);
        return new RenderTransform(scaleX, 0, x, 0, scaleY, y, 0, 0, 1);
    }

    public Box DrawnBox(in RenderTransform placement, in Box canvasBox)
    {
        if (placement.IsIdentity)
        {
            return canvasBox;
        }

        return placement.TryMapBounds(canvasBox, out var drawn) ? drawn : canvasBox;
    }

    public int ParkTarget(CanvasWarpTransform map, CanvasWarp side, in Box canvasBox)
    {
        var vertical = ReferenceEquals(side, map.Top) || ReferenceEquals(side, map.Bottom);
        if (side.IsIdentity)
        {
            return vertical ? canvasBox.Y : canvasBox.X;
        }

        var size = vertical ? canvasBox.Height : canvasBox.Width;
        var direction = side.Direction;
        var fit = int.MinValue;
        var least = double.PositiveInfinity;
        var leastAt = 0;
        for (var step = 0; step <= side.Extension; step++)
        {
            var inner = side.Seam + (direction * step);
            var origin = direction < 0 ? inner - size : inner;
            var box = vertical ? canvasBox with { Y = origin } : canvasBox with { X = origin };
            var placement = PlacementFor(map, box);
            var outer = direction < 0 ? origin : origin + size;
            var drawn = vertical ? (placement.M22 * outer) + placement.M23 : (placement.M11 * outer) + placement.M13;
            var over = direction * (drawn - side.ScreenEdge);
            if (over <= 1e-6)
            {
                fit = origin;
            }

            if (over < least)
            {
                least = over;
                leastAt = origin;
            }
        }

        return fit != int.MinValue ? fit : leastAt;
    }

    public (int X, int Y) CornerParkTarget(CanvasWarpTransform map, CanvasWarp side, CanvasWarp end, in Box canvasBox)
    {
        if (side.IsIdentity || end.IsIdentity)
        {
            return (canvasBox.X, canvasBox.Y);
        }

        var steps = Math.Max(side.Extension, end.Extension);
        var fitX = int.MinValue;
        var fitY = int.MinValue;
        var least = double.PositiveInfinity;
        var leastX = canvasBox.X;
        var leastY = canvasBox.Y;
        for (var step = 0; step <= steps; step++)
        {
            var t = (double)step / steps;
            var innerX = (int)Math.Round(side.Seam + (side.Direction * t * side.Extension));
            var innerY = (int)Math.Round(end.Seam + (end.Direction * t * end.Extension));
            var originX = side.Direction < 0 ? innerX - canvasBox.Width : innerX;
            var originY = end.Direction < 0 ? innerY - canvasBox.Height : innerY;
            var box = new Box(originX, originY, canvasBox.Width, canvasBox.Height);
            var placement = PlacementFor(map, box);
            var outerX = side.Direction < 0 ? originX : originX + canvasBox.Width;
            var outerY = end.Direction < 0 ? originY : originY + canvasBox.Height;
            var overX = side.Direction * ((placement.M11 * outerX) + placement.M13 - side.ScreenEdge);
            var overY = end.Direction * ((placement.M22 * outerY) + placement.M23 - end.ScreenEdge);
            if (overX <= 1e-6 && overY <= 1e-6)
            {
                fitX = originX;
                fitY = originY;
            }

            var over = Math.Max(0.0, overX) + Math.Max(0.0, overY);
            if (over < least)
            {
                least = over;
                leastX = originX;
                leastY = originY;
            }
        }

        return fitX != int.MinValue ? (fitX, fitY) : (leastX, leastY);
    }

    public (double Scale, double X, double Y) ResizeCursor(
        CanvasWarpTransform map,
        in Box start,
        bool left,
        bool right,
        bool top,
        bool bottom,
        double fixedScreenX,
        double fixedScreenY,
        double cursorX,
        double cursorY)
    {
        var fixedX = left ? start.Right : start.X;
        var fixedY = top ? start.Bottom : start.Y;
        var resizeX = left || right;
        var resizeY = top || bottom;
        var low = _minScale;
        var high = 1.0;
        if (Residual(map, start, left, resizeX, top, resizeY, fixedX, fixedY, fixedScreenX, fixedScreenY, cursorX, cursorY, high) >= 0)
        {
            low = high;
        }
        else
        {
            for (var i = 0; i < ResizeIterations; i++)
            {
                var middle = 0.5 * (low + high);
                if (Residual(map, start, left, resizeX, top, resizeY, fixedX, fixedY, fixedScreenX, fixedScreenY, cursorX, cursorY, middle) >= 0)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
        }

        var k = low;
        return (k, fixedX + ((cursorX - fixedScreenX) / k), fixedY + ((cursorY - fixedScreenY) / k));
    }

    private double Residual(
        CanvasWarpTransform map,
        in Box start,
        bool left,
        bool resizeX,
        bool top,
        bool resizeY,
        int fixedX,
        int fixedY,
        double fixedScreenX,
        double fixedScreenY,
        double cursorX,
        double cursorY,
        double k)
    {
        var box = start;
        if (resizeX)
        {
            var width = Math.Max(1, (int)Math.Round(Math.Abs(cursorX - fixedScreenX) / k));
            box = box with { X = left ? fixedX - width : fixedX, Width = width };
        }

        if (resizeY)
        {
            var height = Math.Max(1, (int)Math.Round(Math.Abs(cursorY - fixedScreenY) / k));
            box = box with { Y = top ? fixedY - height : fixedY, Height = height };
        }

        return ScaleFor(map, box) - k;
    }

    public double FitFor(CanvasWarpTransform map, in Box canvasBox, double shelfMin) =>
        Math.Min(
            AxisFit(Across(map.Left, map.Right, canvasBox.X, canvasBox.Width), canvasBox.Width, shelfMin),
            AxisFit(Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height), canvasBox.Height, shelfMin));

    public double FitAtDepth(CanvasWarpTransform map, in Box canvasBox, double shelfMin, double leadingX, double leadingY) =>
        Math.Min(
            AxisFitAtDepth(Across(map.Left, map.Right, canvasBox.X, canvasBox.Width), canvasBox.Width, shelfMin, leadingX),
            AxisFitAtDepth(Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height), canvasBox.Height, shelfMin, leadingY));

    public double SolveFit(CanvasWarpTransform map, in Box canvasBox, double shelfMin, double anchorX, double anchorY)
    {
        var rest = FitFor(map, canvasBox, shelfMin);
        if (rest >= 1.0)
        {
            return 1.0;
        }

        var sideX = Across(map.Left, map.Right, canvasBox.X, canvasBox.Width);
        var sideY = Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height);
        if (FitResidual(map, canvasBox, shelfMin, sideX, sideY, anchorX, anchorY, 1.0) <= 0)
        {
            return 1.0;
        }

        var low = rest;
        var high = 1.0;
        for (var i = 0; i < ResizeIterations; i++)
        {
            var middle = 0.5 * (low + high);
            if (FitResidual(map, canvasBox, shelfMin, sideX, sideY, anchorX, anchorY, middle) > 0)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return 0.5 * (low + high);
    }

    public double ShelfScaleFor(CanvasWarpTransform map, in Box canvasBox, double shelfMin)
    {
        var sideX = Across(map.Left, map.Right, canvasBox.X, canvasBox.Width);
        var sideY = Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height);
        var k = Math.Min(
            sideX is { Terrace: true } ? sideX.EdgeScale : 1.0,
            sideY is { Terrace: true } ? sideY.EdgeScale : 1.0);
        return k * FitFor(map, canvasBox, shelfMin);
    }

    public int TerraceParkTarget(CanvasWarpTransform map, CanvasWarp side, in Box canvasBox, double shelfMin)
    {
        var vertical = ReferenceEquals(side, map.Top) || ReferenceEquals(side, map.Bottom);
        if (side.IsIdentity || !side.Terrace)
        {
            return ParkTarget(map, side, canvasBox);
        }

        var size = vertical ? canvasBox.Height : canvasBox.Width;
        var fit = AxisFit(side, size, shelfMin);
        var outer = side.FarEdge + (side.Direction * side.ShelfWidth / side.EdgeScale);
        var center = outer - (side.Direction * size * fit / 2.0);
        return (int)Math.Round(center - (size / 2.0));
    }

    public (int X, int Y) TerraceCornerParkTarget(
        CanvasWarpTransform map, CanvasWarp side, CanvasWarp end, in Box canvasBox, double shelfMin)
    {
        if (side.IsIdentity || end.IsIdentity || !side.Terrace || !end.Terrace)
        {
            return CornerParkTarget(map, side, end, canvasBox);
        }

        return TerraceCornerTarget(map, side, end, canvasBox, shelfMin, side.OuterEdge, end.OuterEdge);
    }

    public (int X, int Y) TerraceCornerTarget(
        CanvasWarpTransform map, CanvasWarp side, CanvasWarp end, in Box canvasBox, double shelfMin, double outerX, double outerY)
    {
        var width = canvasBox.Width;
        var height = canvasBox.Height;
        var fit = Math.Min(
            AxisFitAtDepth(side, width, shelfMin, outerX),
            AxisFitAtDepth(end, height, shelfMin, outerY));
        var even = Math.Min(side.EdgeScale, end.EdgeScale);
        var fitX = map.Separable ? fit * even / side.EdgeScale : fit;
        var fitY = map.Separable ? fit * even / end.EdgeScale : fit;
        var (outerCanvasX, outerCanvasY) = map.FromWarpPoint(outerX, outerY);
        var x = outerCanvasX - (width / 2.0) - (side.Direction * width * fitX / 2.0);
        var y = outerCanvasY - (height / 2.0) - (end.Direction * height * fitY / 2.0);
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    public (double Scale, double X, double Y) TerraceResizeCursor(
        CanvasWarpTransform map,
        in Box start,
        bool left,
        bool right,
        bool top,
        bool bottom,
        double fixedScreenX,
        double fixedScreenY,
        double cursorX,
        double cursorY,
        double shelfMin)
    {
        var fixedX = left ? start.Right : start.X;
        var fixedY = top ? start.Bottom : start.Y;
        var resizeX = left || right;
        var resizeY = top || bottom;
        var high = ShelfScaleFor(map, start, 1.0) * map.ViewScale;
        var low = Math.Min(Math.Max(shelfMin, 0.01) * map.ViewScale, high);
        if (TerraceResidual(map, start, left, resizeX, top, resizeY, fixedX, fixedY, fixedScreenX, fixedScreenY, cursorX, cursorY, shelfMin, high) >= 0)
        {
            low = high;
        }
        else
        {
            for (var i = 0; i < ResizeIterations; i++)
            {
                var middle = 0.5 * (low + high);
                if (TerraceResidual(map, start, left, resizeX, top, resizeY, fixedX, fixedY, fixedScreenX, fixedScreenY, cursorX, cursorY, shelfMin, middle) >= 0)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
        }

        var k = low;
        return (k, fixedX + ((cursorX - fixedScreenX) / k), fixedY + ((cursorY - fixedScreenY) / k));
    }

    private double TerraceResidual(
        CanvasWarpTransform map,
        in Box start,
        bool left,
        bool resizeX,
        bool top,
        bool resizeY,
        int fixedX,
        int fixedY,
        double fixedScreenX,
        double fixedScreenY,
        double cursorX,
        double cursorY,
        double shelfMin,
        double k)
    {
        var box = start;
        if (resizeX)
        {
            var width = Math.Max(1, (int)Math.Round(Math.Abs(cursorX - fixedScreenX) / k));
            box = box with { X = left ? fixedX - width : fixedX, Width = width };
        }

        if (resizeY)
        {
            var height = Math.Max(1, (int)Math.Round(Math.Abs(cursorY - fixedScreenY) / k));
            box = box with { Y = top ? fixedY - height : fixedY, Height = height };
        }

        return (ShelfScaleFor(map, box, shelfMin) * map.ViewScale) - k;
    }

    private double FitResidual(
        CanvasWarpTransform map,
        in Box canvasBox,
        double shelfMin,
        CanvasWarp? sideX,
        CanvasWarp? sideY,
        double anchorX,
        double anchorY,
        double fit)
    {
        var leadingX = Leading(sideX, anchorX, canvasBox.X, canvasBox.Width, fit);
        var leadingY = Leading(sideY, anchorY, canvasBox.Y, canvasBox.Height, fit);
        return fit - FitAtDepth(map, canvasBox, shelfMin, leadingX, leadingY);
    }

    private static double Leading(CanvasWarp? warp, double anchor, int start, int size, double fit)
    {
        if (warp is null)
        {
            return 0.0;
        }

        double outer = warp.Direction < 0 ? start : start + size;
        return warp.ToScreen(anchor + ((outer - anchor) * fit));
    }

    private static double AxisFit(CanvasWarp? warp, int size, double shelfMin)
    {
        if (warp is not { IsIdentity: false, Terrace: true } || size <= 0)
        {
            return 1.0;
        }

        var k = warp.EdgeScale;
        return Math.Min(1.0, Math.Max(warp.ShelfWidth / (size * k), shelfMin / k));
    }

    private static double AxisFitAtDepth(CanvasWarp? warp, int size, double shelfMin, double leading)
    {
        var rest = AxisFit(warp, size, shelfMin);
        if (rest >= 1.0 || warp is null)
        {
            return 1.0;
        }

        var span = warp.ZoneWidth + warp.ShelfWidth;
        var t = span > 0 ? Math.Clamp(warp.Direction * (leading - warp.Seam) / span, 0.0, 1.0) : 1.0;
        var eased = t * t * (3.0 - (2.0 * t));
        return 1.0 + ((rest - 1.0) * eased);
    }

    private static double ScreenDepth(CanvasWarp? warp, double screen) =>
        warp is null || warp.IsIdentity ? 0.0 : warp.DepthAtScreen(screen);

    private double Floor(double k) => k >= 1.0 ? 1.0 : Math.Max(_minScale, k);

    private static CanvasWarp? Across(CanvasWarp? low, CanvasWarp? high, int start, int size)
    {
        var lowOver = low is { IsIdentity: false } ? low.Seam - start : 0;
        var highOver = high is { IsIdentity: false } ? start + size - high.Seam : 0;
        if (lowOver <= 0 && highOver <= 0)
        {
            return null;
        }

        return lowOver >= highOver ? low : high;
    }

    private (double Scale, double Anchor) Axis(CanvasWarp? warp, int start, int size)
    {
        if (warp is null)
        {
            return (1.0, start + (size / 2.0));
        }

        var direction = warp.Direction;
        double inner = direction < 0 ? start + size : start;
        double outer = direction < 0 ? start : start + size;
        if (direction * (inner - warp.Seam) > 0)
        {
            return (AxisScale(warp, inner), inner);
        }

        var past = size > 0 ? Math.Clamp(direction * (outer - warp.Seam) / size, 0.0, 1.0) : 1.0;
        return (1.0, inner + (direction * size * (1.0 - past) / 2.0));
    }
}
