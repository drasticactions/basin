using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class PrimarySelectionModule : DesktopModule<PrimarySelectionManager>
{
    public override string WireInterface => "zwp_primary_selection_device_manager_v1";

    public override int Version => PrimarySelectionManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ISelectionStore)];

    protected override PrimarySelectionManager Create(BasinServices services) =>
        new(services.Display, services.Find<ISelectionStore>(), services.Find<Seat.Seat>());
}
