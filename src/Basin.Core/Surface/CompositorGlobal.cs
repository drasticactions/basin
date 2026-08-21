using Wayland;
using Wayland.Server;

namespace Basin;

public sealed class CompositorGlobal : IDisposable
{
    public const int Version = 7;

    private readonly WlGlobal _global;
    private readonly Dictionary<WlRegionResource, Region> _regions = [];
    private readonly Dictionary<WlSurfaceResource, Surface> _surfaces = [];

    public CompositorGlobal(WlServerDisplay display, ClientBufferRegistry buffers)
    {
        Buffers = buffers;
        _global = display.CreateGlobal(WlCompositor.Interface, Version, OnBind);
    }

    public ClientBufferRegistry Buffers { get; }

    public event Action<Surface>? SurfaceCreated;

    public IReadOnlyCollection<Surface> Surfaces => _surfaces.Values;

    public void Dispose() => _global.Dispose();

    public Surface? ResolveSurface(WlSurfaceResource? resource) =>
        resource is not null && _surfaces.TryGetValue(resource, out var surface) ? surface : null;

    public Region? ResolveRegion(WlRegionResource? resource) =>
        resource is not null && _regions.TryGetValue(resource, out var region) ? region : null;

    private void OnBind(WlClient client, uint version, uint id)
    {
        var compositor = new WlCompositorResource(client, version, id);

        compositor.CreateSurface += (_, e) =>
        {
            var resource = new WlSurfaceResource(client, compositor.Version, e.Id);
            var surface = new Surface(this, resource);
            _surfaces[resource] = surface;
            surface.Destroyed += () => _surfaces.Remove(resource);
            SurfaceCreated?.Invoke(surface);
        };

        compositor.CreateRegion += (_, e) =>
        {
            var resource = new WlRegionResource(client, compositor.Version, e.Id);
            var region = new Region(resource);
            _regions[resource] = region;
            resource.Destroyed += (_, _) => _regions.Remove(resource);
        };
    }
}
