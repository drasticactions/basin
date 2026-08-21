using Basin.Capabilities;

namespace Basin.Desktop;

public sealed class DrmCapabilityPack : ICapabilityPack
{
    private readonly Basin.Backend.Drm.DrmBackend? _drm;
    private readonly IRenderer _renderer;

    public DrmCapabilityPack(IRenderer renderer, Basin.Backend.Drm.DrmBackend? drm)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        _drm = drm;
    }

    public Basin.Backend.Drm.DrmOutputGamma Gamma { get; } = new();

    public Basin.Backend.Drm.DrmOutputPower Power { get; } = new();

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
