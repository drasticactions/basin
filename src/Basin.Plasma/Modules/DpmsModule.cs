using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class DpmsModule : PlasmaModule<DpmsManager>
{
    public override string WireInterface => "org_kde_kwin_dpms_manager";

    public override int Version => DpmsManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IOutputPower)];

    protected override DpmsManager Create(BasinServices services) =>
        new(services.Display, services.Find<IOutputPower>());
}
