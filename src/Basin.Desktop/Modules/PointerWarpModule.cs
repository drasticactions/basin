using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class PointerWarpModule : DesktopModule<PointerWarpManager>
{
    public override string WireInterface => "wp_pointer_warp_v1";

    public override int Version => PointerWarpManager.Version;

    protected override PointerWarpManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Require<Basin.Seat.Seat>());
}
