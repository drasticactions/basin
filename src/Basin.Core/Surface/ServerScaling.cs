using Pixman;

namespace Basin;

public static class ServerScaling
{
    public static int ToLogical(int value, double serverScale) =>
        serverScale == 1.0 ? value : (int)Math.Round(value / serverScale, MidpointRounding.AwayFromZero);

    public static Box ToLogicalOutward(in Box box, double serverScale)
    {
        if (serverScale == 1.0)
        {
            return box;
        }

        var x1 = (int)Math.Floor(box.X / serverScale);
        var y1 = (int)Math.Floor(box.Y / serverScale);
        var x2 = (int)Math.Ceiling(box.Right / serverScale);
        var y2 = (int)Math.Ceiling(box.Bottom / serverScale);
        return new Box(x1, y1, x2 - x1, y2 - y1);
    }

    public static Box ToLogicalNearest(in Box box, double serverScale)
    {
        if (serverScale == 1.0)
        {
            return box;
        }

        var x1 = ToLogical(box.X, serverScale);
        var y1 = ToLogical(box.Y, serverScale);
        return new Box(x1, y1, ToLogical(box.Right, serverScale) - x1, ToLogical(box.Bottom, serverScale) - y1);
    }

    public static void ToLogical(PixmanRegion32 region, double serverScale, bool outward, PixmanRegion32 scratch)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(scratch);
        if (serverScale == 1.0 || region.IsEmpty)
        {
            return;
        }

        scratch.Clear();
        foreach (var box in RegionRects.Of(region))
        {
            int x1, y1, x2, y2;
            if (outward)
            {
                x1 = (int)Math.Floor(box.X1 / serverScale);
                y1 = (int)Math.Floor(box.Y1 / serverScale);
                x2 = (int)Math.Ceiling(box.X2 / serverScale);
                y2 = (int)Math.Ceiling(box.Y2 / serverScale);
            }
            else
            {
                x1 = (int)Math.Ceiling(box.X1 / serverScale);
                y1 = (int)Math.Ceiling(box.Y1 / serverScale);
                x2 = (int)Math.Floor(box.X2 / serverScale);
                y2 = (int)Math.Floor(box.Y2 / serverScale);
            }

            if (x2 > x1 && y2 > y1)
            {
                scratch.UnionRect(scratch, x1, y1, (uint)(x2 - x1), (uint)(y2 - y1));
            }
        }

        region.Copy(scratch);
    }

    public static void ToLogicalOutward(ref DamageRects rects, double serverScale)
    {
        if (serverScale == 1.0 || rects.Count == 0)
        {
            return;
        }

        var scaled = default(DamageRects);
        for (var i = 0; i < rects.Count; i++)
        {
            scaled.Add(ToLogicalOutward(rects[i], serverScale));
        }

        rects = scaled;
    }
}
