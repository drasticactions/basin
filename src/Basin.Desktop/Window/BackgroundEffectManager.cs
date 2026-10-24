using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>ext_background_effect_manager_v1</c> global. It advertises blur when the effects capability supports it, and keeps each surface's committed blur region.
/// </summary>
public sealed class BackgroundEffectManager : IDisposable
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
        var manager = new ExtBackgroundEffectManagerV1Resource(client, version, id);
        manager.SendCapabilities((ExtBackgroundEffectManagerV1.Capability)_effects);
        manager.GetBackgroundEffect += (_, e) =>
        {
            var resource = new ExtBackgroundEffectSurfaceV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (!_claimed.Add(surface))
            {
                manager.PostError(ErrorBackgroundEffectExists, "The surface already has a background effect object.");
                return;
            }

            var alive = true;
            resource.SetBlurRegion += (_, re) =>
            {
                if (!alive)
                {
                    resource.PostError(ErrorSurfaceDestroyed, "The wl_surface is destroyed.");
                    return;
                }

                var payload = new BlurRegion();
                if (_compositor.ResolveRegion(re.Region) is { } region)
                {
                    payload.Region.Copy(region.Pixman);
                }

                surface.Pending.SetExtension(payload);
            };
            resource.Destroyed += (_, _) =>
            {
                _claimed.Remove(surface);
                if (alive)
                {
                    surface.Pending.SetExtension(new BlurRegion());
                }
            };
            surface.Destroyed += () =>
            {
                alive = false;
                _claimed.Remove(surface);
            };
        };
    }

    private sealed class BlurRegion : IDisposable
    {
        public PixmanRegion32 Region { get; } = new();

        public void Dispose() => Region.Dispose();
    }
}
