using Pixman;

namespace Basin;

/// <summary>
/// Converts client surface coordinates to logical coordinates by dividing by a server scale. A scale of 1 returns each value unchanged.
/// </summary>
public static class ServerScaling
{
    /// <summary>Converts one coordinate, rounded to the nearest integer with midpoints away from zero.</summary>
    /// <param name="value">The value in client units.</param>
    /// <param name="serverScale">The server scale.</param>
    /// <returns>The value in logical units.</returns>
    public static int ToLogical(int value, double serverScale) =>
        serverScale == 1.0 ? value : (int)Math.Round(value / serverScale, MidpointRounding.AwayFromZero);

    /// <summary>Converts a box and rounds its edges outward, so the result covers the source.</summary>
    /// <param name="box">The box in client units.</param>
    /// <param name="serverScale">The server scale.</param>
    /// <returns>The box in logical units.</returns>
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

    /// <summary>Converts a box and rounds each edge to the nearest integer.</summary>
    /// <param name="box">The box in client units.</param>
    /// <param name="serverScale">The server scale.</param>
    /// <returns>The box in logical units.</returns>
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

    /// <summary>Converts a region in place. A rectangle that rounds to nothing is dropped.</summary>
    /// <param name="region">The region to convert.</param>
    /// <param name="serverScale">The server scale.</param>
    /// <param name="outward"><see langword="true"/> to round each rectangle outward, or <see langword="false"/> to round it inward.</param>
    /// <param name="scratch">A work region that the call overwrites.</param>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> or <paramref name="scratch"/> is <see langword="null"/>.</exception>
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

    /// <summary>Converts damage rectangles in place and rounds each one outward.</summary>
    /// <param name="rects">The rectangles to convert.</param>
    /// <param name="serverScale">The server scale.</param>
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
