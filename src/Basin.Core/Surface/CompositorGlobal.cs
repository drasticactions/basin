using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// The <c>wl_compositor</c> global. It creates each <see cref="Surface"/> and <see cref="Region"/> and resolves client resources to them.
/// </summary>
public sealed class CompositorGlobal : IDisposable, IWlCompositorHandler
{
    /// <summary>The <c>wl_compositor</c> version that this global advertises.</summary>
    public const int Version = 7;

    private readonly WlGlobal _global;
    private readonly Dictionary<WlRegionResource, Region> _regions = [];
    private readonly Dictionary<WlSurfaceResource, Surface> _surfaces = [];

    /// <summary>Initializes a new instance of the <see cref="CompositorGlobal"/> class with its own <see cref="ClientScales"/>.</summary>
    /// <param name="display">The display that gets the global.</param>
    /// <param name="buffers">The registry that resolves attached buffers.</param>
    public CompositorGlobal(WlServerDisplay display, ClientBufferRegistry buffers)
        : this(display, buffers, new ClientScales())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CompositorGlobal"/> class.</summary>
    /// <param name="display">The display that gets the global.</param>
    /// <param name="buffers">The registry that resolves attached buffers.</param>
    /// <param name="clientScales">The per-client server scales. The global follows its changes until disposal.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clientScales"/> is <see langword="null"/>.</exception>
    public CompositorGlobal(WlServerDisplay display, ClientBufferRegistry buffers, ClientScales clientScales)
    {
        ArgumentNullException.ThrowIfNull(clientScales);
        Buffers = buffers;
        ClientScales = clientScales;
        clientScales.Changed += OnClientScaleChanged;
        _global = display.CreateGlobal(WlCompositor.Interface, Version, OnBind);
    }

    /// <summary>Gets the registry that resolves each <c>wl_surface.attach</c> buffer.</summary>
    public ClientBufferRegistry Buffers { get; }

    /// <summary>Gets the per-client server scales that new surfaces read.</summary>
    public ClientScales ClientScales { get; }

    /// <summary>Gets the event loop that waits on implicit dmabuf fences. When it is <see langword="null"/>, commits do not wait for implicit fences.</summary>
    public ICompositorEventLoop? Loop { get; init; }

    /// <summary>Gets the per-output sets of the surfaces that each output drew.</summary>
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

    /// <summary>Occurs when a client creates a surface, after the surface is registered.</summary>
    public event Action<Surface>? SurfaceCreated;

    /// <summary>Occurs when a surface sends frame callbacks, with the surface, the timestamp in milliseconds and the callback count.</summary>
    public event Action<Surface, uint, int>? FramesDone;

    internal void NotifyFramesDone(Surface surface, uint timestampMs, int count) => FramesDone?.Invoke(surface, timestampMs, count);

    /// <summary>Gets the live surfaces of all clients.</summary>
    public IReadOnlyCollection<Surface> Surfaces => _surfaces.Values;

    /// <summary>Stops following <see cref="ClientScales"/> and destroys the global.</summary>
    public void Dispose()
    {
        ClientScales.Changed -= OnClientScaleChanged;
        _global.Dispose();
    }

    /// <summary>Finds the surface for a <c>wl_surface</c> resource.</summary>
    /// <param name="resource">The resource, or <see langword="null"/>.</param>
    /// <returns>The surface, or <see langword="null"/> when the resource is <see langword="null"/> or not live.</returns>
    public Surface? ResolveSurface(WlSurfaceResource? resource) =>
        resource is not null && _surfaces.TryGetValue(resource, out var surface) ? surface : null;

    /// <summary>Finds the live region for a <c>wl_region</c> resource. The result is the client's region, not a copy.</summary>
    /// <param name="resource">The resource, or <see langword="null"/>.</param>
    /// <returns>The region, or <see langword="null"/> when the resource is <see langword="null"/> or not live.</returns>
    public Region? ResolveRegion(WlRegionResource? resource) =>
        resource is not null && _regions.TryGetValue(resource, out var region) ? region : null;

    private void OnBind(WlClient client, uint version, uint id)
    {
        _ = new WlCompositorResource(client, version, id, this);
    }

    void IWlCompositorHandler.CreateSurface(WlCompositorResource resource, in WlCompositorResource.CreateSurfaceEventArgs args)
    {
        var client = resource.Client;
        var surface = new Surface(this, client, resource.Version, args.Id, ClientScales.ScaleOf(client));
        var surfaceResource = surface.Resource;
        _surfaces[surfaceResource] = surface;
        surface.Destroyed += () => _surfaces.Remove(surfaceResource);
        SurfaceCreated?.Invoke(surface);
    }

    void IWlCompositorHandler.CreateRegion(WlCompositorResource resource, in WlCompositorResource.CreateRegionEventArgs args)
    {
        var region = new Region(resource.Client, resource.Version, args.Id);
        var regionResource = region.Resource;
        _regions[regionResource] = region;
        regionResource.Destroyed += (_, _) => _regions.Remove(regionResource);
    }

    void IWlCompositorHandler.Release(WlCompositorResource resource)
    {
    }
}
