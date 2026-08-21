using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ColorRepresentationModule : DesktopModule<ColorRepresentationManager>
{
    public override string WireInterface => "wp_color_representation_manager_v1";

    public override int Version => ColorRepresentationManager.Version;

    protected override ColorRepresentationManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
