using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class AlphaModifierModule : DesktopModule<AlphaModifierManager>
{
    public override string WireInterface => "wp_alpha_modifier_v1";

    public override int Version => AlphaModifierManager.Version;

    protected override AlphaModifierManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
