using Basin.Diagnostics;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// A client's <c>wl_region</c>. It is live: the client can change or destroy it after any request, so a caller copies <see cref="Pixman"/> to keep a value.
/// </summary>
public sealed class Region : IWlRegionHandler
{
    /// <summary>Initializes a new instance of the <see cref="Region"/> class and creates its <c>wl_region</c> resource. The region applies <c>add</c> and <c>subtract</c> requests as they arrive and frees itself when the resource is destroyed.</summary>
    /// <param name="client">The client that owns the resource.</param>
    /// <param name="version">The resource version.</param>
    /// <param name="id">The new object id from the client.</param>
    public Region(WlClient client, uint version, uint id)
    {
        Resource = new WlRegionResource(client, version, id, this);
        BasinCounters.Track();

        Resource.Destroyed += (_, _) =>
        {
            Pixman.Dispose();
            BasinCounters.Untrack();
        };
    }

    void IWlRegionHandler.Destroy(WlRegionResource resource)
    {
    }

    void IWlRegionHandler.Add(WlRegionResource resource, in WlRegionResource.AddEventArgs args)
    {
        if (args.Width > 0 && args.Height > 0)
        {
            Pixman.UnionRect(Pixman, args.X, args.Y, (uint)args.Width, (uint)args.Height);
        }
    }

    void IWlRegionHandler.Subtract(WlRegionResource resource, in WlRegionResource.SubtractEventArgs args)
    {
        if (args.Width > 0 && args.Height > 0)
        {
            using var rect = new PixmanRegion32(args.X, args.Y, (uint)args.Width, (uint)args.Height);
            Pixman.SubtractWith(rect);
        }
    }

    /// <summary>Gets the region resource.</summary>
    public WlRegionResource Resource { get; }

    /// <summary>Gets the region in surface-local coordinates. It is disposed when the resource is destroyed.</summary>
    public PixmanRegion32 Pixman { get; } = new();
}
