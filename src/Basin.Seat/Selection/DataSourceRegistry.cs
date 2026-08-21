using System.Runtime.InteropServices;
using Wayland;
using Wayland.Server;

namespace Basin.Seat;

internal static class DataSourceRegistry
{
    private static readonly Dictionary<WlDataSourceResource, DataSource> Sources = [];

    public static void Register(DataSource source)
    {
        if (source.Resource is { } resource)
        {
            Sources[resource] = source;
            resource.Destroyed += (_, _) => Sources.Remove(resource);
        }
    }

    public static DataSource? Resolve(WlDataSourceResource? resource) =>
        resource is not null && Sources.TryGetValue(resource, out var source) ? source : null;
}
