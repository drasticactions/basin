using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// The step map: a zoomed desktop plane and a smaller shelf plane about one center, joined by walls that hold no canvas point. Screen coordinates are in logical pixels.
/// </summary>
public sealed class CanvasStepMap
{
    /// <summary>
    /// Gets the X coordinate of the center that both planes scale about.
    /// </summary>
    public double CenterX { get; private set; }

    /// <summary>
    /// Gets the Y coordinate of the center that both planes scale about.
    /// </summary>
    public double CenterY { get; private set; }

    /// <summary>
    /// Gets the scale of the desktop plane.
    /// </summary>
    public double Zoom { get; private set; } = 1.0;

    /// <summary>
    /// Gets the scale of the shelf plane. It is at most <see cref="Zoom"/> in a useful layout.
    /// </summary>
    public double ShelfScale { get; private set; } = 1.0;

    /// <summary>
    /// Gets the screen outline of the desktop, where the walls start.
    /// </summary>
    public FBox Outline { get; private set; }

    /// <summary>
    /// Gets the screen rectangle of the base lines, where the walls meet the shelves.
    /// </summary>
    public FBox Inner { get; private set; }

    /// <summary>
    /// Gets the screen rectangle that the map covers, usually the output.
    /// </summary>
    public FBox Outer { get; private set; }

    /// <summary>
    /// Gets the sides that have a wall.
    /// </summary>
    public CanvasStepSides Sides { get; private set; }

    /// <summary>
    /// Gets the sides that have a shelf past the wall. It is always a subset of <see cref="Sides"/>.
    /// </summary>
    public CanvasStepSides Shelves { get; private set; }

    /// <summary>
    /// Gets a value indicating whether both planes have a scale of 1.
    /// </summary>
    public bool IsIdentity => Zoom == 1.0 && ShelfScale == 1.0;

    /// <summary>
    /// The deepest bend that <see cref="SetBulge"/> accepts. Above it the bend can fold over.
    /// </summary>
    public const double MaxBulge = 0.7;

    private const int BendIterations = 8;

    private readonly double[] _bulgeCenter = new double[4];
    private readonly double[] _bulgeDepth = new double[4];

    /// <summary>
    /// Gets or sets a value indicating whether the sources cut their wall lines and columns finely enough to draw a bend.
    /// </summary>
    public bool Bulges { get; set; }

    /// <summary>
    /// Bends the rows of one wall toward a row. The planes and the miters do not move.
    /// </summary><param name="side">One wall side. A combined or empty value is ignored.</param>
    /// <param name="center">The row coordinate along the wall that the bend pulls toward.</param>
    /// <param name="depth">The bend depth. The method clamps it to the range from 0 to <see cref="MaxBulge"/> and reads a value that is not finite as 0.</param>
    /// <returns><see langword="true"/> when the bend of the side changed.</returns>
    public bool SetBulge(CanvasStepSides side, double center, double depth)
    {
        var index = BulgeIndex(side);
        depth = double.IsFinite(depth) ? Math.Clamp(depth, 0.0, MaxBulge) : 0.0;
        if (index < 0 || (_bulgeCenter[index] == center && _bulgeDepth[index] == depth))
        {
            return false;
        }

        _bulgeCenter[index] = center;
        _bulgeDepth[index] = depth;
        return true;
    }

    /// <summary>
    /// Returns the row that a wall bends toward.
    /// </summary><param name="side">One wall side. A combined or empty value is ignored.</param>
    /// <returns>The center, or 0 for a value that is not one side.</returns>
    public double BulgeCenter(CanvasStepSides side) => BulgeIndex(side) is >= 0 and var index ? _bulgeCenter[index] : 0.0;

    /// <summary>
    /// Returns the bend depth of a wall.
    /// </summary><param name="side">One wall side. A combined or empty value is ignored.</param>
    /// <returns>The depth, or 0 for a value that is not one side.</returns>
    public double BulgeDepth(CanvasStepSides side) => BulgeIndex(side) is >= 0 and var index ? _bulgeDepth[index] : 0.0;

    /// <summary>
    /// Returns the bend strength at a depth across a wall. It is 0 at the desktop edge and at the base line.
    /// </summary><param name="side">One wall side. A combined or empty value is ignored.</param>
    /// <param name="t">The depth across the wall, from 0 to 1. The method clamps it.</param>
    /// <returns>The bend depth times a parabola that peaks at the middle of the wall.</returns>
    public double BulgeAt(CanvasStepSides side, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return BulgeDepth(side) * 4.0 * t * (1.0 - t);
    }

    /// <summary>
    /// Returns the extent of a wall along its length at a depth across it. It interpolates from the outline edge to the base line.
    /// </summary>
    /// <param name="side">One wall side.</param>
    /// <param name="t">The depth across the wall, 0 at the desktop edge and 1 at the base line.</param>
    /// <returns>The start coordinate and the length along the wall.</returns>
    public (double Start, double Length) WallSpan(CanvasStepSides side, double t)
    {
        var vertical = side is CanvasStepSides.Left or CanvasStepSides.Right;
        var innerStart = vertical ? Outline.Y : Outline.X;
        var innerLength = vertical ? Outline.Height : Outline.Width;
        var outerStart = vertical ? Inner.Y : Inner.X;
        var outerLength = vertical ? Inner.Height : Inner.Width;
        return (innerStart + ((outerStart - innerStart) * t), innerLength + ((outerLength - innerLength) * t));
    }

    /// <summary>
    /// Applies the bend of a wall to a point on it.
    /// </summary>
    /// <param name="side">One wall side.</param>
    /// <param name="x">The screen X coordinate of the point.</param>
    /// <param name="y">The screen Y coordinate of the point.</param>
    /// <param name="t">The depth of the point across the wall.</param>
    /// <returns>The bent point. It equals the input when the wall has no bend.</returns>
    public (double X, double Y) Bend(CanvasStepSides side, double x, double y, double t)
    {
        var k = BulgeAt(side, t);
        if (k == 0)
        {
            return (x, y);
        }

        var (start, length) = WallSpan(side, t);
        var center = BulgeCenter(side);
        return side is CanvasStepSides.Left or CanvasStepSides.Right
            ? (x, BendAlong(y, k, center, start, length))
            : (BendAlong(x, k, center, start, length), y);
    }

    /// <summary>
    /// Removes the bend of a wall from a point with eight Newton steps. It is the inverse of <see cref="Bend"/>.
    /// </summary>
    /// <param name="side">One wall side.</param>
    /// <param name="x">The bent screen X coordinate.</param>
    /// <param name="y">The bent screen Y coordinate.</param>
    /// <param name="t">The depth of the point across the wall.</param>
    /// <returns>The unbent point.</returns>
    public (double X, double Y) Unbend(CanvasStepSides side, double x, double y, double t)
    {
        var k = BulgeAt(side, t);
        if (k == 0)
        {
            return (x, y);
        }

        var (start, length) = WallSpan(side, t);
        var center = BulgeCenter(side);
        return side is CanvasStepSides.Left or CanvasStepSides.Right
            ? (x, UnbendAlong(y, k, center, start, length))
            : (UnbendAlong(x, k, center, start, length), y);
    }

    private static double BendAlong(double value, double k, double center, double start, double length)
    {
        if (length <= 0)
        {
            return value;
        }

        var a = Math.Clamp((value - start) / length, 0.0, 1.0);
        return value + (k * 4.0 * a * (1.0 - a) * (center - value));
    }

    private static double UnbendAlong(double bent, double k, double center, double start, double length)
    {
        if (length <= 0)
        {
            return bent;
        }

        var value = bent;
        for (var i = 0; i < BendIterations; i++)
        {
            var a = Math.Clamp((value - start) / length, 0.0, 1.0);
            var envelope = 4.0 * a * (1.0 - a);
            var slope = 4.0 * (1.0 - (2.0 * a)) / length;
            var f = value + (k * envelope * (center - value)) - bent;
            var df = 1.0 + (k * ((slope * (center - value)) - envelope));
            value -= f / df;
        }

        return value;
    }

    private static int BulgeIndex(CanvasStepSides side) => side switch
    {
        CanvasStepSides.Left => 0,
        CanvasStepSides.Right => 1,
        CanvasStepSides.Top => 2,
        CanvasStepSides.Bottom => 3,
        _ => -1,
    };

    /// <summary>
    /// Lays out the map with a shelf on every side that has a wall.
    /// </summary><param name="centerX">The X coordinate of the scale center on screen.</param>
    /// <param name="centerY">The Y coordinate of the scale center on screen.</param>
    /// <param name="zoom">The scale of the desktop plane.</param>
    /// <param name="shelfScale">The scale of the shelf plane.</param>
    /// <param name="outline">The screen outline of the desktop.</param>
    /// <param name="inner">The screen rectangle of the base lines.</param>
    /// <param name="outer">The screen rectangle that the map covers.</param>
    /// <param name="sides">The sides that have a wall.</param>
    /// <returns><see langword="true"/> when any input changed.</returns>
    public bool Layout(
        double centerX, double centerY, double zoom, double shelfScale, in FBox outline, in FBox inner, in FBox outer, CanvasStepSides sides) =>
        Layout(centerX, centerY, zoom, shelfScale, outline, inner, outer, sides, sides);

    /// <summary>
    /// Lays out the map. A zoom or shelf scale that is not positive and finite counts as 1.
    /// </summary><param name="centerX">The X coordinate of the scale center on screen.</param>
    /// <param name="centerY">The Y coordinate of the scale center on screen.</param>
    /// <param name="zoom">The scale of the desktop plane.</param>
    /// <param name="shelfScale">The scale of the shelf plane.</param>
    /// <param name="outline">The screen outline of the desktop.</param>
    /// <param name="inner">The screen rectangle of the base lines.</param>
    /// <param name="outer">The screen rectangle that the map covers.</param>
    /// <param name="sides">The sides that have a wall.</param>
    /// <param name="shelves">The sides that also have a shelf. The method keeps only the sides that are in <paramref name="sides"/>.</param>
    /// <returns><see langword="true"/> when any input changed.</returns>
    public bool Layout(
        double centerX, double centerY, double zoom, double shelfScale, in FBox outline, in FBox inner, in FBox outer,
        CanvasStepSides sides, CanvasStepSides shelves)
    {
        zoom = zoom > 0 && double.IsFinite(zoom) ? zoom : 1.0;
        shelfScale = shelfScale > 0 && double.IsFinite(shelfScale) ? shelfScale : 1.0;
        shelves &= sides;
        if (CenterX == centerX && CenterY == centerY && Zoom == zoom && ShelfScale == shelfScale &&
            Outline == outline && Inner == inner && Outer == outer && Sides == sides && Shelves == shelves)
        {
            return false;
        }

        Shelves = shelves;

        CenterX = centerX;
        CenterY = centerY;
        Zoom = zoom;
        ShelfScale = shelfScale;
        Outline = outline;
        Inner = inner;
        Outer = outer;
        Sides = sides;
        return true;
    }

    /// <summary>
    /// Returns the scale of a plane.
    /// </summary>
    /// <param name="plane">The plane.</param>
    /// <returns><see cref="ShelfScale"/> for the shelf, otherwise <see cref="Zoom"/>.</returns>
    public double ScaleOf(CanvasStepPlane plane) => plane == CanvasStepPlane.Shelf ? ShelfScale : Zoom;

    /// <summary>
    /// Returns the screen width of a wall across its depth.
    /// </summary>
    /// <param name="side">One wall side.</param>
    /// <returns>0 for a side with no wall or a value that is not one side.</returns>
    public double WallWidth(CanvasStepSides side)
    {
        if ((Sides & side) != side || side == CanvasStepSides.None)
        {
            return 0.0;
        }

        return side switch
        {
            CanvasStepSides.Left => Outline.X - Inner.X,
            CanvasStepSides.Right => Inner.Right - Outline.Right,
            CanvasStepSides.Top => Outline.Y - Inner.Y,
            CanvasStepSides.Bottom => Inner.Bottom - Outline.Bottom,
            _ => 0.0,
        };
    }

    /// <summary>
    /// Returns the plane that a canvas point is on. It is the shelf when its shelf image lies past the base line of a side with a shelf.
    /// </summary><param name="canvasX">The canvas X coordinate.</param>
    /// <param name="canvasY">The canvas Y coordinate.</param>
    /// <returns>The plane of the point.</returns>
    public CanvasStepPlane PlaneAt(double canvasX, double canvasY)
    {
        var x = CenterX + ((canvasX - CenterX) * ShelfScale);
        var y = CenterY + ((canvasY - CenterY) * ShelfScale);
        var shelf = ((Shelves & CanvasStepSides.Left) != 0 && x <= Inner.X) ||
            ((Shelves & CanvasStepSides.Right) != 0 && x >= Inner.Right) ||
            ((Shelves & CanvasStepSides.Top) != 0 && y <= Inner.Y) ||
            ((Shelves & CanvasStepSides.Bottom) != 0 && y >= Inner.Bottom);
        return shelf ? CanvasStepPlane.Shelf : CanvasStepPlane.Desktop;
    }

    /// <summary>
    /// Returns the plane that the center of a canvas box is on.
    /// </summary>
    /// <param name="canvasBox">The window box in canvas pixels.</param>
    /// <returns>The plane of the box center.</returns>
    public CanvasStepPlane PlaneOf(in Box canvasBox) =>
        PlaneAt(canvasBox.X + (canvasBox.Width / 2.0), canvasBox.Y + (canvasBox.Height / 2.0));

    /// <summary>
    /// Returns the matrix of a window on the desktop plane.
    /// </summary><param name="preScale">The scale of the window about its anchor before the plane scale.</param>
    /// <param name="anchorX">The canvas X coordinate that the pre-scale leaves in place.</param>
    /// <param name="anchorY">The canvas Y coordinate that the pre-scale leaves in place.</param>
    /// <returns>The placement matrix.</returns>
    public RenderTransform DesktopPlacement(double preScale, double anchorX, double anchorY) =>
        Placement(CanvasStepPlane.Desktop, preScale, anchorX, anchorY);

    /// <summary>
    /// Returns the matrix of a window on the shelf plane.
    /// </summary><param name="preScale">The scale of the window about its anchor before the plane scale.</param>
    /// <param name="anchorX">The canvas X coordinate that the pre-scale leaves in place.</param>
    /// <param name="anchorY">The canvas Y coordinate that the pre-scale leaves in place.</param>
    /// <returns>The placement matrix.</returns>
    public RenderTransform ShelfPlacement(double preScale, double anchorX, double anchorY) =>
        Placement(CanvasStepPlane.Shelf, preScale, anchorX, anchorY);

    /// <summary>
    /// Returns the matrix of a window on a plane: a pre-scale about an anchor, then the plane scale about the center.
    /// </summary>
    /// <param name="plane">The plane.</param>
    /// <param name="preScale">The scale of the window about its anchor before the plane scale.</param>
    /// <param name="anchorX">The canvas X coordinate that the pre-scale leaves in place.</param>
    /// <param name="anchorY">The canvas Y coordinate that the pre-scale leaves in place.</param>
    /// <returns>The placement matrix, or <see cref="RenderTransform.Identity"/> when both scales are 1.</returns>
    public RenderTransform Placement(CanvasStepPlane plane, double preScale, double anchorX, double anchorY)
    {
        var scale = ScaleOf(plane);
        if (scale == 1.0 && preScale == 1.0)
        {
            return RenderTransform.Identity;
        }

        var total = scale * preScale;
        return new RenderTransform(
            total, 0, (CenterX * (1.0 - scale)) + (scale * anchorX * (1.0 - preScale)),
            0, total, (CenterY * (1.0 - scale)) + (scale * anchorY * (1.0 - preScale)),
            0, 0, 1);
    }

    /// <summary>
    /// Maps a canvas point to the screen through the plane it is on.
    /// </summary><param name="canvasX">The canvas X coordinate.</param>
    /// <param name="canvasY">The canvas Y coordinate.</param>
    /// <returns>The screen point.</returns>
    public (double X, double Y) ToScreen(double canvasX, double canvasY) =>
        ToScreen(PlaneAt(canvasX, canvasY), canvasX, canvasY);

    /// <summary>
    /// Maps a canvas point to the screen through a given plane.
    /// </summary>
    /// <param name="plane">The plane.</param>
    /// <param name="canvasX">The canvas X coordinate.</param>
    /// <param name="canvasY">The canvas Y coordinate.</param>
    /// <returns>The screen point.</returns>
    public (double X, double Y) ToScreen(CanvasStepPlane plane, double canvasX, double canvasY)
    {
        var scale = ScaleOf(plane);
        return (CenterX + ((canvasX - CenterX) * scale), CenterY + ((canvasY - CenterY) * scale));
    }

    /// <summary>
    /// Maps a screen point back to the canvas through a given plane.
    /// </summary>
    /// <param name="plane">The plane.</param>
    /// <param name="screenX">The screen X coordinate.</param>
    /// <param name="screenY">The screen Y coordinate.</param>
    /// <returns>The canvas point.</returns>
    public (double X, double Y) ToCanvas(CanvasStepPlane plane, double screenX, double screenY)
    {
        var scale = ScaleOf(plane);
        return (CenterX + ((screenX - CenterX) / scale), CenterY + ((screenY - CenterY) / scale));
    }

    /// <summary>
    /// Maps a screen point back to the canvas through the plane it lies on. A point on a wall has no canvas point.
    /// </summary>
    /// <param name="screenX">The screen X coordinate.</param>
    /// <param name="screenY">The screen Y coordinate.</param>
    /// <param name="plane">The plane of the point.</param>
    /// <param name="canvasX">The canvas X coordinate, or the screen X coordinate on failure.</param>
    /// <param name="canvasY">The canvas Y coordinate, or the screen Y coordinate on failure.</param>
    /// <returns><see langword="false"/> outside <see cref="Outer"/>, on a wall, and past a wall that has no shelf.</returns>
    public bool TryToCanvas(double screenX, double screenY, out CanvasStepPlane plane, out double canvasX, out double canvasY)
    {
        plane = CanvasStepPlane.Desktop;
        canvasX = screenX;
        canvasY = screenY;
        if (screenX < Outer.X || screenX >= Outer.Right || screenY < Outer.Y || screenY >= Outer.Bottom)
        {
            return false;
        }

        var depth = WallDepth(screenX, screenY, out var side);
        if (depth > 0.0 && (depth < 1.0 || (side & Shelves) != side))
        {
            return false;
        }

        plane = depth >= 1.0 ? CanvasStepPlane.Shelf : CanvasStepPlane.Desktop;
        (canvasX, canvasY) = ToCanvas(plane, screenX, screenY);
        return true;
    }

    /// <summary>
    /// Maps a screen point to the canvas through the nearest plane. It uses the desktop plane up to the middle of a wall and the shelf plane after it.
    /// </summary>
    /// <param name="screenX">The screen X coordinate.</param>
    /// <param name="screenY">The screen Y coordinate.</param>
    /// <param name="plane">The plane that the method used.</param>
    /// <returns>The canvas point.</returns>
    public (double X, double Y) ToCanvasNearest(double screenX, double screenY, out CanvasStepPlane plane)
    {
        plane = WallDepth(screenX, screenY, out _) > 0.5 ? CanvasStepPlane.Shelf : CanvasStepPlane.Desktop;
        return ToCanvas(plane, screenX, screenY);
    }

    /// <summary>
    /// Returns the depth of a screen point across the deepest wall. It is 0 on the desktop edge, 1 on the base line, below 0 on the desktop and above 1 on a shelf.
    /// </summary>
    /// <param name="screenX">The screen X coordinate.</param>
    /// <param name="screenY">The screen Y coordinate.</param>
    /// <param name="side">The side that owns the point. In a corner past both base lines, the two sides combined.</param>
    /// <returns>The depth, or negative infinity when the map has no wall.</returns>
    public double WallDepth(double screenX, double screenY, out CanvasStepSides side)
    {
        var across = double.NegativeInfinity;
        var along = double.NegativeInfinity;
        var horizontal = CanvasStepSides.None;
        var vertical = CanvasStepSides.None;
        Depth(CanvasStepSides.Left, -(screenX - Outline.X), ref across, ref horizontal);
        Depth(CanvasStepSides.Right, screenX - Outline.Right, ref across, ref horizontal);
        Depth(CanvasStepSides.Top, -(screenY - Outline.Y), ref along, ref vertical);
        Depth(CanvasStepSides.Bottom, screenY - Outline.Bottom, ref along, ref vertical);
        if (horizontal == CanvasStepSides.None && vertical == CanvasStepSides.None)
        {
            side = CanvasStepSides.None;
            return double.NegativeInfinity;
        }

        if (across >= 1.0 && along >= 1.0)
        {
            side = horizontal | vertical;
            return Math.Max(across, along);
        }

        side = across >= along ? horizontal : vertical;
        return Math.Max(across, along);
    }

    private void Depth(CanvasStepSides side, double past, ref double deepest, ref CanvasStepSides owner)
    {
        var width = WallWidth(side);
        if (width <= 0.0)
        {
            return;
        }

        var depth = past / width;
        if (depth > deepest)
        {
            deepest = depth;
            owner = side;
        }
    }

    /// <summary>
    /// Returns the scale of a held window at a depth. It eases from the desktop zoom to the shelf scale, flat at both ends.
    /// </summary>
    /// <param name="depth">The wall depth. The method clamps it to the range from 0 to 1 and reads NaN as 0.</param>
    /// <returns>The draw scale.</returns>
    public double DragScale(double depth)
    {
        var t = Math.Clamp(double.IsNaN(depth) ? 0.0 : depth, 0.0, 1.0);
        var eased = t * t * (3.0 - (2.0 * t));
        return Zoom + ((ShelfScale - Zoom) * eased);
    }

    /// <summary>
    /// Returns the screen rectangle of a shelf. A combination of two sides gives their corner square.
    /// </summary>
    /// <param name="side">One side, or two adjacent sides for a corner.</param>
    /// <returns>The strip, or an empty box when a named side has no shelf.</returns>
    public FBox ShelfStrip(CanvasStepSides side)
    {
        if (side == CanvasStepSides.None || (Shelves & side) != side)
        {
            return default;
        }

        var left = Outer.X;
        var top = Outer.Y;
        var right = Outer.Right;
        var bottom = Outer.Bottom;
        if ((side & CanvasStepSides.Left) != 0)
        {
            right = Math.Min(right, Inner.X);
        }

        if ((side & CanvasStepSides.Right) != 0)
        {
            left = Math.Max(left, Inner.Right);
        }

        if ((side & CanvasStepSides.Top) != 0)
        {
            bottom = Math.Min(bottom, Inner.Y);
        }

        if ((side & CanvasStepSides.Bottom) != 0)
        {
            top = Math.Max(top, Inner.Bottom);
        }

        return right <= left || bottom <= top ? default : new FBox(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Returns the fit shrink that makes a window fit a shelf strip. The smaller of the two axes applies.
    /// </summary>
    /// <param name="width">The window width in canvas pixels.</param>
    /// <param name="height">The window height in canvas pixels.</param>
    /// <param name="side">The strip, as for <see cref="ShelfStrip"/>.</param>
    /// <param name="minScale">The least scale at which a window can draw on the shelf.</param>
    /// <returns>A factor up to 1, which is 1 when the strip is empty or the window fits.</returns>
    public double ShelfFit(double width, double height, CanvasStepSides side, double minScale)
    {
        var strip = ShelfStrip(side);
        if (strip.IsEmpty || width <= 0 || height <= 0)
        {
            return 1.0;
        }

        var floor = minScale / ShelfScale;
        var fitX = Math.Min(1.0, Math.Max(floor, strip.Width / (width * ShelfScale)));
        var fitY = Math.Min(1.0, Math.Max(floor, strip.Height / (height * ShelfScale)));
        return Math.Min(fitX, fitY);
    }
}
