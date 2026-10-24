using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>ext_background_effect_manager_v1</c> global. It advertises blur when the effects capability supports it, and keeps each surface's committed blur region.
/// </summary>
public sealed class BackgroundEffectManager : IDisposable, IExtBackgroundEffectManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorBackgroundEffectExists = 0;
    private const uint ErrorSurfaceDestroyed = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly BackgroundEffects _effects;
    private readonly HashSet<Surface> _claimed = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundEffectManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global that resolves surfaces.</param>
    /// <param name="effects">The supported effects, or <see langword="null"/> to advertise none.</param>
    public BackgroundEffectManager(WlServerDisplay display, CompositorGlobal compositor, IBackgroundEffects? effects)
    {
        _compositor = compositor;
        _effects = (effects?.Supported ?? BackgroundEffects.None) & BackgroundEffects.Blur;
        _global = display.CreateGlobal(ExtBackgroundEffectManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Gets the blur region of the surface's current state.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The region in surface-local logical pixels, or <see langword="null"/> when the surface has none. The manager owns it.</returns>
    public PixmanRegion32? BlurRegionOf(Surface surface) =>
        surface.Current.GetExtension<BlurRegion>()?.Region;

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ExtBackgroundEffectManagerV1Resource(client, version, id, this);
        manager.SendCapabilities((ExtBackgroundEffectManagerV1.Capability)_effects);
    }

    void IExtBackgroundEffectManagerV1Handler.GetBackgroundEffect(ExtBackgroundEffectManagerV1Resource resource, in ExtBackgroundEffectManagerV1Resource.GetBackgroundEffectEventArgs args)
    {
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            _ = new ExtBackgroundEffectSurfaceV1Resource(resource.Client, resource.Version, args.Id, IgnoredEffect.Instance);
            return;
        }

        if (!_claimed.Add(surface))
        {
            _ = new ExtBackgroundEffectSurfaceV1Resource(resource.Client, resource.Version, args.Id, IgnoredEffect.Instance);
            resource.PostError(ErrorBackgroundEffectExists, "The surface already has a background effect object.");
            return;
        }

        _ = new EffectSurface(this, resource.Client, resource.Version, args.Id, surface);
    }

    void IExtBackgroundEffectManagerV1Handler.Destroy(ExtBackgroundEffectManagerV1Resource resource)
    {
    }

    private sealed class EffectSurface : IExtBackgroundEffectSurfaceV1Handler
    {
        private readonly BackgroundEffectManager _owner;
        private readonly Surface _surface;
        private bool _alive = true;

        public EffectSurface(BackgroundEffectManager owner, WlClient client, uint version, uint id, Surface surface)
        {
            _owner = owner;
            _surface = surface;
            var resource = new ExtBackgroundEffectSurfaceV1Resource(client, version, id, this);
            resource.Destroyed += OnResourceDestroyed;
            surface.Destroyed += OnSurfaceDestroyed;
        }

        void IExtBackgroundEffectSurfaceV1Handler.SetBlurRegion(ExtBackgroundEffectSurfaceV1Resource resource, in ExtBackgroundEffectSurfaceV1Resource.SetBlurRegionEventArgs args)
        {
            if (!_alive)
            {
                resource.PostError(ErrorSurfaceDestroyed, "The wl_surface is destroyed.");
                return;
            }

            var payload = new BlurRegion();
            if (_owner._compositor.ResolveRegion(args.Region) is { } region)
            {
                payload.Region.Copy(region.Pixman);
            }

            _surface.Pending.SetExtension(payload);
        }

        void IExtBackgroundEffectSurfaceV1Handler.Destroy(ExtBackgroundEffectSurfaceV1Resource resource)
        {
        }

        private void OnResourceDestroyed(object? sender, EventArgs e)
        {
            _owner._claimed.Remove(_surface);
            if (_alive)
            {
                _surface.Pending.SetExtension(new BlurRegion());
            }
        }

        private void OnSurfaceDestroyed()
        {
            _alive = false;
            _owner._claimed.Remove(_surface);
        }
    }

    private sealed class IgnoredEffect : IExtBackgroundEffectSurfaceV1Handler
    {
        public static readonly IgnoredEffect Instance = new();

        void IExtBackgroundEffectSurfaceV1Handler.SetBlurRegion(ExtBackgroundEffectSurfaceV1Resource resource, in ExtBackgroundEffectSurfaceV1Resource.SetBlurRegionEventArgs args)
        {
        }

        void IExtBackgroundEffectSurfaceV1Handler.Destroy(ExtBackgroundEffectSurfaceV1Resource resource)
        {
        }
    }

    private sealed class BlurRegion : IDisposable
    {
        public PixmanRegion32 Region { get; } = new();

        public void Dispose() => Region.Dispose();
    }
}
