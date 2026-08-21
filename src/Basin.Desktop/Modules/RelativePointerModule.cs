using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class RelativePointerModule : DesktopModule<RelativePointerManager>
{
    public override string WireInterface => "zwp_relative_pointer_manager_v1";

    public override int Version => RelativePointerManager.Version;

    protected override RelativePointerManager Create(BasinServices services) =>
        new(services.Display, services.Require<Seat.Seat>());
}
