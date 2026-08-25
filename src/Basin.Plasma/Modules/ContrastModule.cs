using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class ContrastModule : PlasmaModule<ContrastManager>
{
    public override string WireInterface => "org_kde_kwin_contrast_manager";

    public override int Version => ContrastManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IBackgroundEffects)];

    public override IDisposable Install(BasinServices services)
    {
        var installed = base.Install(services);
        services.UseDefault<IBackgroundContrast>(Manager!);
        return installed;
    }

    protected override ContrastManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<IBackgroundEffects>());
}
