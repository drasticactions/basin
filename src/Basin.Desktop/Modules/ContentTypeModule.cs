using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ContentTypeModule : DesktopModule<ContentTypeManager>
{
    public override string WireInterface => "wp_content_type_manager_v1";

    public override int Version => ContentTypeManager.Version;

    protected override ContentTypeManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
