using Basin;

namespace Basin.Portal.Client;

internal static class RegionCapturePlan
{
    public static double TargetScale(Box box, ReadOnlySpan<Box> outputs, ReadOnlySpan<double> scales, double requested)
    {
        if (requested > 0)
        {
            return requested;
        }

        double scale = 1;
        for (var i = 0; i < outputs.Length; i++)
        {
            if (!outputs[i].Intersect(box).IsEmpty)
            {
                scale = Math.Max(scale, scales[i]);
            }
        }

        return scale;
    }

    public static Box Size(Box box, double targetScale)
    {
        var physical = OutputScaling.ToPhysical(box, targetScale);
        return new Box(0, 0, physical.Width, physical.Height);
    }

    public static int Plan(Box box, ReadOnlySpan<Box> outputs, ReadOnlySpan<double> scales, double targetScale, Box crop, Span<RegionPart> parts)
    {
        var origin = OutputScaling.ToPhysical(box, targetScale);
        var bounds = new Box(0, 0, origin.Width, origin.Height);
        if (!crop.IsEmpty)
        {
            bounds = bounds.Intersect(crop);
        }

        var count = 0;
        for (var i = 0; i < outputs.Length && count < parts.Length; i++)
        {
            var area = outputs[i].Intersect(box);
            if (area.IsEmpty)
            {
                continue;
            }

            var physical = OutputScaling.ToPhysical(area, targetScale);
            var local = new Box(physical.X - origin.X, physical.Y - origin.Y, physical.Width, physical.Height).Intersect(bounds);
            if (local.IsEmpty)
            {
                continue;
            }

            var ratio = scales[i] / targetScale;
            var sourceX = (ratio * local.X) + ((((double)origin.X / targetScale) - outputs[i].X) * scales[i]);
            var sourceY = (ratio * local.Y) + ((((double)origin.Y / targetScale) - outputs[i].Y) * scales[i]);
            var destination = crop.IsEmpty ? local : new Box(local.X - crop.X, local.Y - crop.Y, local.Width, local.Height);
            parts[count++] = new RegionPart(i, destination, sourceX, sourceY, ratio);
        }

        return count;
    }
}
