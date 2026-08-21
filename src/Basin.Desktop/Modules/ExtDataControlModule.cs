using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ExtDataControlModule : DesktopModule<ExtDataControlManager>
{
    public override string WireInterface => "ext_data_control_manager_v1";

    public override int Version => ExtDataControlManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ISelectionStore)];

    protected override ExtDataControlManager Create(BasinServices services) =>
        new(services.Display, services.Find<ISelectionStore>());
}
