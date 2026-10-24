using Basin.Diagnostics;
using Pixman;
using Wayland;

namespace Basin;

/// <summary>
/// A client's <c>wl_region</c>. It is live: the client can change or destroy it after any request, so a caller copies <see cref="Pixman"/> to keep a value.
/// </summary>
public sealed class Region
{
    /// <summary>Initializes a new instance of the <see cref="Region"/> class. The region applies <c>add</c> and <c>subtract</c> requests as they arrive and frees itself when the resource is destroyed.</summary>
    /// <param name="resource">The region resource.</param>
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

    /// <summary>Gets the region resource.</summary>
    public WlRegionResource Resource { get; }

    /// <summary>Gets the region in surface-local coordinates. It is disposed when the resource is destroyed.</summary>
    public PixmanRegion32 Pixman { get; } = new();
}
