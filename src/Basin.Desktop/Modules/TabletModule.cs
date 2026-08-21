using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class TabletModule : DesktopModule<TabletManager>
{
    public override string WireInterface => "zwp_tablet_manager_v2";

    public override int Version => TabletManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ITabletSource)];

    protected override TabletManager Create(BasinServices services) =>
        new(services.Display, services.Find<ITabletSource>());
}
