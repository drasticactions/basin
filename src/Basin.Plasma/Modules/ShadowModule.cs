namespace Basin.Plasma;

public sealed class ShadowModule : PlasmaModule<ShadowManager>
{
    public override string WireInterface => "org_kde_kwin_shadow_manager";

    public override int Version => ShadowManager.Version;

    protected override ShadowManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
