using Basin;
using Basin.Effects;

namespace TinyComp;

internal static class OverviewLayout
{
    public const double MaxEdgeScale = 0.98;

    public static int MinSlope(int size) => Math.Max(8, (int)Math.Round(0.01 * size));

    public static OverviewSide Full(int outer, int usable, double center, int direction, double scale, double shelfFraction, int size)
    {
        var zoomed = center + ((outer - center) * scale);
        var border = (int)Math.Floor(direction * (usable - zoomed));
        var least = MinSlope(size);
        if (border < least + 8)
        {
            return new OverviewSide(false, border, 0, 0);
        }

        var shelf = (int)Math.Round(shelfFraction * size);
        var slope = border - shelf;
        if (slope < least)
        {
            shelf = border - least;
            slope = least;
        }

        return new OverviewSide(true, border, shelf, slope);
    }

    public static OverviewSide WallFull(int outer, int usable, double center, int direction, double scale, int least)
    {
        var zoomed = center + ((outer - center) * scale);
        var border = (int)Math.Floor(direction * (usable - zoomed));
        return border < Math.Max(1, least) ? new OverviewSide(false, border, 0, 0) : new OverviewSide(true, border, 0, border);
    }

    public static CanvasStepSides StepSidesOf(CanvasSide sides)
    {
        var step = CanvasStepSides.None;
        step |= (sides & CanvasSide.Left) != 0 ? CanvasStepSides.Left : 0;
        step |= (sides & CanvasSide.Right) != 0 ? CanvasStepSides.Right : 0;
        step |= (sides & CanvasSide.Top) != 0 ? CanvasStepSides.Top : 0;
        step |= (sides & CanvasSide.Bottom) != 0 ? CanvasStepSides.Bottom : 0;
        return step;
    }

    public static int MinShelf(int size) => Math.Max(16, (int)Math.Round(0.02 * size));

    public static OverviewSide StepFull(int outer, int usable, double center, int direction, double scale, double wallFraction, int size)
    {
        var zoomed = center + ((outer - center) * scale);
        var border = (int)Math.Floor(direction * (usable - zoomed));
        var wall = (int)Math.Round(wallFraction * size);
        if (wall < 1 || border < wall + MinShelf(size))
        {
            return new OverviewSide(false, border, 0, 0);
        }

        return new OverviewSide(true, border, border - wall, wall);
    }

    public static double StepWallAt(in OverviewSide full, double progress, int outer, int usable, double center, int direction, double scale)
    {
        if (!full.Active || full.Border <= 0)
        {
            return 0.0;
        }

        progress = Math.Clamp(progress, 0.0, 1.0);
        var zoom = 1.0 + ((scale - 1.0) * progress);
        var border = direction * (usable - (center + ((outer - center) * zoom)));
        return border <= 0.0 ? 0.0 : full.Slope * border / full.Border;
    }

    public static bool LayoutStep(
        CanvasStepMap map, in Box box, in Box usable, ReadOnlySpan<OverviewSide> full, double progress, double scale, double shelfScale) =>
        LayoutStep(
            map, box, usable, full, progress, scale, shelfScale, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0),
            CanvasStepSides.Left | CanvasStepSides.Right | CanvasStepSides.Top | CanvasStepSides.Bottom);

    public static bool LayoutStep(
        CanvasStepMap map, in Box box, in Box usable, ReadOnlySpan<OverviewSide> full, double progress, double scale, double shelfScale,
        double centerX, double centerY, CanvasStepSides shelves)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        var zoom = 1.0 + ((scale - 1.0) * progress);
        var shelf = 1.0 + ((Math.Min(shelfScale, scale) - 1.0) * progress);
        var left = EdgeAt(box.X, centerX, zoom);
        var right = EdgeAt(box.Right, centerX, zoom);
        var top = EdgeAt(box.Y, centerY, zoom);
        var bottom = EdgeAt(box.Bottom, centerY, zoom);
        var wallLeft = StepWallAt(full[0], progress, box.X, usable.X, centerX, -1, scale);
        var wallRight = StepWallAt(full[1], progress, box.Right, usable.Right, centerX, 1, scale);
        var wallTop = StepWallAt(full[2], progress, box.Y, usable.Y, centerY, -1, scale);
        var wallBottom = StepWallAt(full[3], progress, box.Bottom, usable.Bottom, centerY, 1, scale);
        var sides = CanvasStepSides.None;
        sides |= full[0].Active ? CanvasStepSides.Left : 0;
        sides |= full[1].Active ? CanvasStepSides.Right : 0;
        sides |= full[2].Active ? CanvasStepSides.Top : 0;
        sides |= full[3].Active ? CanvasStepSides.Bottom : 0;
        var outline = new FBox(left, top, right - left, bottom - top);
        var innerLeft = left - wallLeft;
        var innerTop = top - wallTop;
        var inner = new FBox(innerLeft, innerTop, right + wallRight - innerLeft, bottom + wallBottom - innerTop);
        return map.Layout(centerX, centerY, zoom, shelf, outline, inner, usable, sides, shelves);
    }

    public static double EdgeAt(int outer, double center, double zoom) => center + ((outer - center) * zoom);

    public static OverviewStep At(
        in OverviewSide full, double progress, int outer, int usable, double center, int direction, double scale, double shelfScale)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        var zoom = 1.0 + ((scale - 1.0) * progress);
        var edge = 1.0 + ((shelfScale - 1.0) * progress);
        if (!full.Active || full.Border <= 0)
        {
            return new OverviewStep(0, 0, 1.0, zoom);
        }

        var border = direction * (usable - (center + ((outer - center) * zoom)));
        if (border < 1.0)
        {
            return new OverviewStep(0, 0, 1.0, zoom);
        }

        var slope = full.Slope * border / full.Border;
        if (slope < 1.0)
        {
            return new OverviewStep(0, 0, 1.0, zoom);
        }

        var zone = Math.Max(1, (int)Math.Round(slope / zoom));
        var total = (int)Math.Round(border / zoom);
        return new OverviewStep(zone, Math.Max(0, total - zone), Math.Min(edge / zoom, MaxEdgeScale), zoom);
    }
}
