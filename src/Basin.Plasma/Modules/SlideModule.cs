namespace Basin.Plasma;

public sealed class SlideModule : PlasmaModule<SlideManager>
{
    public override string WireInterface => "org_kde_kwin_slide_manager";

    public override int Version => SlideManager.Version;

    protected override SlideManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
