using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class SystemBellModule : DesktopModule<SystemBellManager>
{
    public override string WireInterface => "xdg_system_bell_v1";

    public override int Version => SystemBellManager.Version;

    public override IReadOnlyList<Type> Drivers => [typeof(IBell)];

    protected override SystemBellManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<IBell>());
}
