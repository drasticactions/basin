using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class FractionalScaleModule : DesktopModule<FractionalScaleManager>
{
    public override string WireInterface => "wp_fractional_scale_manager_v1";

    public override int Version => FractionalScaleManager.Version;

    protected override FractionalScaleManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<OutputLayout>());
}
