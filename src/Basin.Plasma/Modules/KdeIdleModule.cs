using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class KdeIdleModule : PlasmaModule<KdeIdleManager>
{
    public override string WireInterface => "org_kde_kwin_idle";

    public override int Version => KdeIdleManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IIdleSource)];

    protected override KdeIdleManager Create(BasinServices services) =>
        new(services.Display, services.Loop, services.Find<IIdleSource>());
}
