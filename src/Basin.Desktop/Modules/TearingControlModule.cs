using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class TearingControlModule : DesktopModule<TearingControlManager>
{
    public override string WireInterface => "wp_tearing_control_manager_v1";

    public override int Version => TearingControlManager.Version;

    protected override TearingControlManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
