using Basin.Diagnostics;

namespace Basin.Effects;

/// <summary>
/// Draws a window in a zone as an axis-aligned rectangle that gets smaller with depth, in place of the mesh. Every method is arithmetic on the stack.
/// </summary>
public sealed class CanvasScale
{
    /// <summary>
    /// The default least scale of a window in a zone.
    /// </summary>
    public const double DefaultMinScale = 0.35;

    private const int ResizeIterations = 48;

    private double _minScale = DefaultMinScale;
    private double _reach = 1.0;

    /// <summary>
    /// Gets or sets the least scale of a window in a zone. The setter clamps it to the range from 0.05 to 1.
    /// </summary>
    public double MinScale
    {
        get => _minScale;
        set => _minScale = Math.Clamp(value, 0.05, 1.0);
    }

    /// <summary>
    /// Gets or sets how much canvas depth the shrink spreads over, as a multiple of the extension. The setter clamps it to the range from 1 to 16.
    /// </summary>
    public double Reach
    {
        get => _reach;
        set => _reach = Math.Clamp(value, 1.0, 16.0);
    }

    /// <summary>
    /// Returns the scale on one axis for a window whose inner edge is at a canvas coordinate. It does not apply <see cref="MinScale"/>.
    /// </summary>
    /// <param name="warp">The zone on that axis, or <see langword="null"/> for none.</param>
    /// <param name="inner">The canvas coordinate of the window edge nearest the flat area.</param>
    /// <returns>1 at and before the seam, then the curve of the zone stretched by <see cref="Reach"/>.</returns>
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

    /// <summary>
    /// Returns the scale of a window from the depth of its inner edges.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <returns>The smaller axis scale, at least <see cref="MinScale"/> and at most 1.</returns>
    public double ScaleFor(CanvasWarpTransform map, in Box canvasBox)
    {
        var (kx, _) = Axis(Across(map.Left, map.Right, canvasBox.X, canvasBox.Width), canvasBox.X, canvasBox.Width);
        var (ky, _) = Axis(Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height), canvasBox.Y, canvasBox.Height);
        return Floor(Math.Min(kx, ky));
    }

    /// <summary>
    /// Returns the canvas point that the placement holds on the field. It ramps from the box center to the inner edge while the box straddles the seam.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <returns>The anchor in canvas pixels.</returns>
    public (double X, double Y) AnchorFor(CanvasWarpTransform map, in Box canvasBox)
    {
        var (_, x) = Axis(Across(map.Left, map.Right, canvasBox.X, canvasBox.Width), canvasBox.X, canvasBox.Width);
        var (_, y) = Axis(Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height), canvasBox.Y, canvasBox.Height);
        return (x, y);
    }

    /// <summary>
    /// Returns the placement of a resting window: the scale from its inner edges about its anchor, drawn where the warp puts the anchor.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <returns>The identity while the inner edges are on the flat side of every seam.</returns>
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

    /// <summary>
    /// Returns the eased scale from the depth of a screen point in the deepest zone that holds it.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="screenX">The screen X coordinate.</param>
    /// <param name="screenY">The screen Y coordinate.</param>
    /// <returns>1 outside every zone and <see cref="MinScale"/> at the screen edge.</returns>
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

    /// <summary>
    /// Returns the resting placement for an anchor, drawn at its field position through the same solve as <see cref="HandPlacementFor"/>.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <param name="anchorX">The canvas X coordinate of the anchor.</param>
    /// <param name="anchorY">The canvas Y coordinate of the anchor.</param>
    /// <returns>The placement matrix.</returns>
    public RenderTransform AnchoredPlacementFor(CanvasWarpTransform map, in Box canvasBox, double anchorX, double anchorY)
    {
        var (screenX, screenY) = map.ToScreenPoint(anchorX, anchorY);
        return HandPlacementFor(map, canvasBox, anchorX, anchorY, screenX, screenY);
    }

    /// <summary>
    /// Returns the placement of a held window, with the scale solved from its drawn edges. The window reaches the floor exactly as its leading edge meets the screen edge.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <param name="anchorX">The canvas X coordinate of the grabbed point.</param>
    /// <param name="anchorY">The canvas Y coordinate of the grabbed point.</param>
    /// <param name="screenX">The screen X coordinate where the grabbed point draws.</param>
    /// <param name="screenY">The screen Y coordinate where the grabbed point draws.</param>
    /// <param name="bias">A value added to the eased scale, which lets a consumer blend in from another scale.</param>
    /// <returns>The placement matrix, with a scale from <see cref="MinScale"/> to 1.</returns>
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

    /// <summary>
    /// Returns the placement of a dragged window with the grabbed point as the fixed point of the scale. The scale comes from the inner edges.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <param name="grabX">The canvas X coordinate of the grabbed point.</param>
    /// <param name="grabY">The canvas Y coordinate of the grabbed point.</param>
    /// <param name="cursorX">The screen X coordinate of the cursor.</param>
    /// <param name="cursorY">The screen Y coordinate of the cursor.</param>
    /// <returns>The placement matrix.</returns>
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

    /// <summary>
    /// Returns a uniform scale that draws a canvas point at a screen point.
    /// </summary>
    /// <param name="scale">The uniform scale.</param>
    /// <param name="canvasX">The canvas X coordinate of the fixed point.</param>
    /// <param name="canvasY">The canvas Y coordinate of the fixed point.</param>
    /// <param name="screenX">The screen X coordinate where the point draws.</param>
    /// <param name="screenY">The screen Y coordinate where the point draws.</param>
    /// <returns>The matrix, or <see cref="RenderTransform.Identity"/> when it changes nothing.</returns>
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

    /// <summary>
    /// Interpolates the scale and translation entries of two axis-aligned scales.
    /// </summary>
    /// <param name="from">The placement at 0.</param>
    /// <param name="to">The placement at 1.</param>
    /// <param name="t">The blend fraction. Values outside the range from 0 to 1 return an end.</param>
    /// <returns>An axis-aligned scale. It ignores any rotation or projection in the inputs.</returns>
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

    /// <summary>
    /// Returns the screen box that a placement draws a canvas box into.
    /// </summary>
    /// <param name="placement">The placement matrix.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <returns>The drawn box, or the canvas box when the placement is the identity or cannot map it.</returns>
    public Box DrawnBox(in RenderTransform placement, in Box canvasBox)
    {
        if (placement.IsIdentity)
        {
            return canvasBox;
        }

        return placement.TryMapBounds(canvasBox, out var drawn) ? drawn : canvasBox;
    }

    /// <summary>
    /// Searches for the deepest position on one side at which the drawn outer edge stays on the screen. It samples each canvas pixel from the seam to the far edge.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="side">The zone to park in.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <returns>The canvas origin of the box on that axis, or the position with the least overhang when no position fits.</returns>
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

    /// <summary>
    /// Searches along the diagonal from the seams of a corner for the deepest position at which both drawn outer edges stay on the screen.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="side">The left or right zone of the corner.</param>
    /// <param name="end">The top or bottom zone of the corner.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <returns>The canvas origin of the box, or the position with the least overhang when no position fits.</returns>
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

    /// <summary>
    /// Solves a resize that holds the opposite corner still and puts the drawn grabbed edges on the cursor.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="start">The window box in canvas pixels when the resize started.</param>
    /// <param name="left"><see langword="true"/> when the left edge is grabbed.</param>
    /// <param name="right"><see langword="true"/> when the right edge is grabbed.</param>
    /// <param name="top"><see langword="true"/> when the top edge is grabbed.</param>
    /// <param name="bottom"><see langword="true"/> when the bottom edge is grabbed.</param>
    /// <param name="fixedScreenX">The screen X coordinate of the corner that stays still.</param>
    /// <param name="fixedScreenY">The screen Y coordinate of the corner that stays still.</param>
    /// <param name="cursorX">The screen X coordinate of the cursor.</param>
    /// <param name="cursorY">The screen Y coordinate of the cursor.</param>
    /// <returns>The scale of the resized window and the canvas point under the cursor. The consumer applies its own size rules to that point.</returns>
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

    /// <summary>
    /// Returns the fit shrink of a window at rest on a terrace shelf. In a corner the smaller of the two axes applies.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <param name="shelfMin">The least scale at which a window can draw on a shelf.</param>
    /// <returns>A factor up to 1, which is 1 when the window fits the shelf or no terrace holds it.</returns>
    public double FitFor(CanvasWarpTransform map, in Box canvasBox, double shelfMin) =>
        Math.Min(
            AxisFit(Across(map.Left, map.Right, canvasBox.X, canvasBox.Width), canvasBox.Width, shelfMin),
            AxisFit(Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height), canvasBox.Height, shelfMin));

    /// <summary>
    /// Returns the fit shrink eased in by the screen depth of the leading edges. It is 1 at the seam and <see cref="FitFor"/> at the screen edge.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <param name="shelfMin">The least scale at which a window can draw on a shelf.</param>
    /// <param name="leadingX">The screen X coordinate of the leading edge, in the unzoomed frame.</param>
    /// <param name="leadingY">The screen Y coordinate of the leading edge, in the unzoomed frame.</param>
    /// <returns>The eased factor.</returns>
    public double FitAtDepth(CanvasWarpTransform map, in Box canvasBox, double shelfMin, double leadingX, double leadingY) =>
        Math.Min(
            AxisFitAtDepth(Across(map.Left, map.Right, canvasBox.X, canvasBox.Width), canvasBox.Width, shelfMin, leadingX),
            AxisFitAtDepth(Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height), canvasBox.Height, shelfMin, leadingY));

    /// <summary>
    /// Finds the fit shrink at which the leading edges, drawn through that shrink, give the same shrink back. It solves by bisection.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <param name="shelfMin">The least scale at which a window can draw on a shelf.</param>
    /// <param name="anchorX">The canvas X coordinate of the pre-scale anchor.</param>
    /// <param name="anchorY">The canvas Y coordinate of the pre-scale anchor.</param>
    /// <returns>The fit factor, from <see cref="FitFor"/> to 1.</returns>
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

    /// <summary>
    /// Returns the scale at which a window draws on a terrace shelf: the shelf scale times the fit shrink.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <param name="shelfMin">The least scale at which a window can draw on a shelf.</param>
    /// <returns>The draw scale. It does not include the view zoom.</returns>
    public double ShelfScaleFor(CanvasWarpTransform map, in Box canvasBox, double shelfMin)
    {
        var sideX = Across(map.Left, map.Right, canvasBox.X, canvasBox.Width);
        var sideY = Across(map.Top, map.Bottom, canvasBox.Y, canvasBox.Height);
        var k = Math.Min(
            sideX is { Terrace: true } ? sideX.EdgeScale : 1.0,
            sideY is { Terrace: true } ? sideY.EdgeScale : 1.0);
        return k * FitFor(map, canvasBox, shelfMin);
    }

    /// <summary>
    /// Returns the closed-form park position on a terrace side, with the pre-scaled outer edge on the screen edge and the anchor at the center.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="side">The zone to park in.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <param name="shelfMin">The least scale at which a window can draw on a shelf.</param>
    /// <returns>The canvas origin of the box on that axis. A zone that is not a terrace falls back to <see cref="ParkTarget"/>.</returns>
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

    /// <summary>
    /// Returns the park position in a terrace corner, with the pre-scaled outer corner on the screen corner.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="side">The left or right zone of the corner.</param>
    /// <param name="end">The top or bottom zone of the corner.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <param name="shelfMin">The least scale at which a window can draw on a shelf.</param>
    /// <returns>The canvas origin of the box. A corner that is not two terraces falls back to <see cref="CornerParkTarget"/>.</returns>
    public (int X, int Y) TerraceCornerParkTarget(
        CanvasWarpTransform map, CanvasWarp side, CanvasWarp end, in Box canvasBox, double shelfMin)
    {
        if (side.IsIdentity || end.IsIdentity || !side.Terrace || !end.Terrace)
        {
            return CornerParkTarget(map, side, end, canvasBox);
        }

        return TerraceCornerTarget(map, side, end, canvasBox, shelfMin, side.OuterEdge, end.OuterEdge);
    }

    /// <summary>
    /// Returns the position that puts the pre-scaled outer corner of a meshed window on a point, through the inverse map.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="side">The left or right zone of the corner.</param>
    /// <param name="end">The top or bottom zone of the corner.</param>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <param name="shelfMin">The least scale at which a window can draw on a shelf.</param>
    /// <param name="outerX">The X coordinate of the target point in the unzoomed frame.</param>
    /// <param name="outerY">The Y coordinate of the target point in the unzoomed frame.</param>
    /// <returns>The canvas origin of the box.</returns>
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

    /// <summary>
    /// Solves a resize on a terrace shelf, where the drawn scale is the shelf scale times the fit and the view zoom.
    /// </summary><param name="map">The warp map of the output.</param>
    /// <param name="start">The window box in canvas pixels when the resize started.</param>
    /// <param name="left"><see langword="true"/> when the left edge is grabbed.</param>
    /// <param name="right"><see langword="true"/> when the right edge is grabbed.</param>
    /// <param name="top"><see langword="true"/> when the top edge is grabbed.</param>
    /// <param name="bottom"><see langword="true"/> when the bottom edge is grabbed.</param>
    /// <param name="fixedScreenX">The screen X coordinate of the corner that stays still.</param>
    /// <param name="fixedScreenY">The screen Y coordinate of the corner that stays still.</param>
    /// <param name="cursorX">The screen X coordinate of the cursor.</param>
    /// <param name="cursorY">The screen Y coordinate of the cursor.</param>
    /// <param name="shelfMin">The least scale at which a window can draw on a shelf.</param>
    /// <returns>The drawn scale of the resized window and the canvas point under the cursor.</returns>
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
