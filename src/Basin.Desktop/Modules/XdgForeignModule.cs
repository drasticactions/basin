using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class XdgForeignModule : DesktopModule<XdgForeignManager>
{
    public override string WireInterface => "zxdg_exporter_v2";

    public override int Version => XdgForeignManager.Version;

    protected override XdgForeignManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
