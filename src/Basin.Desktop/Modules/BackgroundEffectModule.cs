using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class BackgroundEffectModule : DesktopModule<BackgroundEffectManager>
{
    public override string WireInterface => "ext_background_effect_manager_v1";

    public override int Version => BackgroundEffectManager.Version;

    protected override BackgroundEffectManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<IBackgroundEffects>());
}
