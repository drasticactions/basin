namespace Basin.Plasma;

public sealed class AppMenuModule : PlasmaModule<AppMenuManager>
{
    public override string WireInterface => "org_kde_kwin_appmenu_manager";

    public override int Version => AppMenuManager.Version;

    protected override AppMenuManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
