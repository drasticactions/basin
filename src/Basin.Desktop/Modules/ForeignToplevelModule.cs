using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ForeignToplevelModule : DesktopModule<ForeignToplevelManager>
{
    public override string WireInterface => "zwlr_foreign_toplevel_manager_v1";

    public override int Version => ForeignToplevelManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel)];

    protected override ForeignToplevelManager Create(BasinServices services) =>
        new(services.Display, services.Find<IToplevelModel>());
}
