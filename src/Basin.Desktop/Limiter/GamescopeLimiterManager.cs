using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the gamescope limiter global. It tells each client whether one of its surfaces has a frame-rate cap.
/// </summary>
public sealed class GamescopeLimiterManager : IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly IFrameLimits _limits;
    private readonly CompositorGlobal _compositor;
    private readonly List<GamescopeLimiterResource> _resources = [];
    private readonly Dictionary<GamescopeLimiterResource, uint> _sent = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="GamescopeLimiterManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global whose surfaces it scans.</param>
    /// <param name="limits">The caps to report.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public GamescopeLimiterManager(WlServerDisplay display, CompositorGlobal compositor, IFrameLimits limits)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(limits);
        _compositor = compositor;
        _limits = limits;
        _limits.Changed += OnChanged;
        _compositor.SurfaceCreated += OnSurfaceCreated;
        _global = display.CreateGlobal(GamescopeLimiter.Interface, Version, OnBind);
    }

    /// <summary>
    /// Stops following the caps and surfaces, and removes the global.
    /// </summary>
    public void Dispose()
    {
        _limits.Changed -= OnChanged;
        _compositor.SurfaceCreated -= OnSurfaceCreated;
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new GamescopeLimiterResource(client, version, id);
        _resources.Add(resource);
        resource.Destroyed += (_, _) =>
        {
            _resources.Remove(resource);
            _sent.Remove(resource);
        };
        Send(resource);
    }

    private void OnSurfaceCreated(Surface surface) => surface.Destroyed += () => OnChanged(surface);

    private void OnChanged(Surface surface)
    {
        foreach (var resource in _resources)
        {
            if (resource.Client == surface.Resource.Client)
            {
                Send(resource);
            }
        }
    }

    private void Send(GamescopeLimiterResource resource)
    {
        var state = ClientLimited(resource.Client) ? 1u : 0u;
        if (_sent.TryGetValue(resource, out var previous) && previous == state)
        {
            return;
        }

        _sent[resource] = state;
        resource.SendState(state);
    }

    private bool ClientLimited(WlClient client)
    {
        foreach (var surface in _compositor.Surfaces)
        {
            if (surface.Resource.Client == client && !surface.IsDestroyed && _limits.LimitOf(surface) > 0)
            {
                return true;
            }
        }

        return false;
    }
}
