using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class DrmLeaseModule : DesktopModule<DrmLeaseManager>
{
    public override string WireInterface => "wp_drm_lease_device_v1";

    public override int Version => DrmLeaseManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IDrmLeaseDevice)];

    protected override DrmLeaseManager Create(BasinServices services) =>
        new(services.Display, services.Find<IDrmLeaseDevice>());
}
