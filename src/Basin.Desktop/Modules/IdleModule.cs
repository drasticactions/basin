using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class IdleModule : DesktopModule<IdleManager>
{
    public override string WireInterface => "ext_idle_notifier_v1";

    public override int Version => 1;

    public override IReadOnlyList<Type> Capabilities => [typeof(IIdleSource)];

    protected override IdleManager Create(BasinServices services) =>
        new(services.Display, services.Loop, services.Require<CompositorGlobal>(), services.Find<IIdleSource>());
}
