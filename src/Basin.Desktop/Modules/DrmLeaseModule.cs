using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_drm_lease_device_v1</c> global through a <see cref="DrmLeaseManager"/>. It offers connectors from <see cref="IDrmLeaseDevice"/> when one is registered.
/// </summary>
public sealed class DrmLeaseModule : DesktopModule<DrmLeaseManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_drm_lease_device_v1";

    /// <inheritdoc/>
    public override int Version => DrmLeaseManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IDrmLeaseDevice)];

    /// <inheritdoc/>
    protected override DrmLeaseManager Create(BasinServices services) =>
        new(services.Display, services.Find<IDrmLeaseDevice>());
}
