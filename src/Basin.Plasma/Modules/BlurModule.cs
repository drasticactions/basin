using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class BlurModule : PlasmaModule<BlurManager>
{
    public override string WireInterface => "org_kde_kwin_blur_manager";

    public override int Version => BlurManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IBackgroundEffects)];

    protected override BlurManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<IBackgroundEffects>());
}
