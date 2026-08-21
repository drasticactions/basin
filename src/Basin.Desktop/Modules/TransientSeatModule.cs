using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class TransientSeatModule : DesktopModule<TransientSeatManager>
{
    public override string WireInterface => "ext_transient_seat_manager_v1";

    public override int Version => TransientSeatManager.Version;

    protected override TransientSeatManager Create(BasinServices services) =>
        new(services.Display, services.Find<CompositorGlobal>(), services.Find<IKeymapSource>());
}
