using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

internal static class XdgSurfaceRegistry
{
    private static readonly Dictionary<XdgSurfaceResource, XdgSurfaceState> Surfaces = [];

    public static void Register(XdgSurfaceResource resource, XdgSurfaceState state) => Surfaces[resource] = state;

    public static void Remove(XdgSurfaceResource resource) => Surfaces.Remove(resource);

    public static XdgSurfaceState? Resolve(XdgSurfaceResource? resource) =>
        resource is not null && Surfaces.TryGetValue(resource, out var state) ? state : null;
}
