using Basin.Diagnostics;
using Pixman;
using Wayland;

namespace Basin;

public sealed class Region
{
    public Region(WlRegionResource resource)
    {
        Resource = resource;
        BasinCounters.Track();

        resource.Add += (_, e) =>
        {
            if (e.Width > 0 && e.Height > 0)
            {
                Pixman.UnionRect(Pixman, e.X, e.Y, (uint)e.Width, (uint)e.Height);
            }
        };

        resource.Subtract += (_, e) =>
        {
            if (e.Width > 0 && e.Height > 0)
            {
                using var rect = new PixmanRegion32(e.X, e.Y, (uint)e.Width, (uint)e.Height);
                Pixman.SubtractWith(rect);
            }
        };

        resource.Destroyed += (_, _) =>
        {
            Pixman.Dispose();
            BasinCounters.Untrack();
        };
    }

    public WlRegionResource Resource { get; }

    public PixmanRegion32 Pixman { get; } = new();
}
