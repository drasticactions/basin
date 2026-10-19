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
        : this(display, buffers, new ClientScales())
    {
    }

    public CompositorGlobal(WlServerDisplay display, ClientBufferRegistry buffers, ClientScales clientScales)
    {
        ArgumentNullException.ThrowIfNull(clientScales);
        Buffers = buffers;
        ClientScales = clientScales;
        clientScales.Changed += OnClientScaleChanged;
        _global = display.CreateGlobal(WlCompositor.Interface, Version, OnBind);
    }

    public ClientBufferRegistry Buffers { get; }

    public ClientScales ClientScales { get; }

    public ICompositorEventLoop? Loop { get; init; }

    public PresentedSets PresentedSets { get; init; } = new();

    private void OnClientScaleChanged(WlClient client, double scale)
    {
        foreach (var surface in _surfaces.Values)
        {
            if (surface.Resource.Client == client)
            {
                surface.SetClientScale(scale);
            }
        }
    }

    public event Action<Surface>? SurfaceCreated;

    public event Action<Surface, uint, int>? FramesDone;

    internal void NotifyFramesDone(Surface surface, uint timestampMs, int count) => FramesDone?.Invoke(surface, timestampMs, count);

    public IReadOnlyCollection<Surface> Surfaces => _surfaces.Values;

    public void Dispose()
    {
        ClientScales.Changed -= OnClientScaleChanged;
        _global.Dispose();
    }

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
            var surface = new Surface(this, resource, ClientScales.ScaleOf(client));
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
