using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class PointerGesturesModule : DesktopModule<PointerGesturesManager>
{
    public override string WireInterface => "zwp_pointer_gestures_v1";

    public override int Version => PointerGesturesManager.Version;

    protected override PointerGesturesManager Create(BasinServices services) =>
        new(services.Display, services.Require<Seat.Seat>());
}
