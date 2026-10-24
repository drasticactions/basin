using Basin.Capabilities;

namespace Basin.Desktop;

/// <summary>
/// Registers the output gamma, output power, DRM lease and DRM sync object capabilities.
/// </summary>
public sealed class DrmCapabilityPack : ICapabilityPack
{
    private readonly Basin.Backend.Drm.DrmBackend? _drm;
    private readonly IRenderer _renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrmCapabilityPack"/> class.
    /// </summary>
    /// <param name="renderer">The renderer whose DRM device supplies the sync object fd.</param>
    /// <param name="drm">The DRM backend that supplies leasing and the fallback fd, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public DrmCapabilityPack(IRenderer renderer, Basin.Backend.Drm.DrmBackend? drm)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        _drm = drm;
    }

    /// <summary>
    /// Gets the gamma control that the pack registers as <see cref="IOutputGamma"/>.
    /// </summary>
    public Basin.Backend.Drm.DrmOutputGamma Gamma { get; } = new();

    /// <summary>
    /// Gets the power control that the pack registers as <see cref="IOutputPower"/>.
    /// </summary>
    public Basin.Backend.Drm.DrmOutputPower Power { get; } = new();

    /// <summary>
    /// Registers gamma and power always. It registers leasing only with a DRM backend, and sync objects only when the device supports timelines.
    /// </summary>
    /// <param name="services">The registry to add to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public void Register(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Use<IOutputGamma>(Gamma);
        services.Use<IOutputPower>(Power);

        if (_drm is { } card)
        {
            services.Use<IDrmLeaseDevice>(card.Leasing);
        }

        var syncFd = _renderer.Device?.DrmFd ?? _drm?.Device.Fd ?? -1;
        if (Basin.Backend.Drm.DrmSyncDevice.SupportsTimelines(syncFd))
        {
            services.Use<IDrmSyncDevice>(new Basin.Backend.Drm.DrmSyncDevice(syncFd));
        }
    }
}
