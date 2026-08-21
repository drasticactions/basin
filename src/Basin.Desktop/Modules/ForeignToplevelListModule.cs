using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ForeignToplevelListModule : DesktopModule<ForeignToplevelListManager>
{
    public override string WireInterface => "ext_foreign_toplevel_list_v1";

    public override int Version => ForeignToplevelListManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel)];

    protected override ForeignToplevelListManager Create(BasinServices services) =>
        new(services.Display, services.Find<IToplevelModel>());
}
