using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class DataControlModule : DesktopModule<DataControlManager>
{
    public override string WireInterface => "zwlr_data_control_manager_v1";

    public override int Version => DataControlManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ISelectionStore)];

    protected override DataControlManager Create(BasinServices services) =>
        new(services.Display, services.Find<ISelectionStore>());
}
