using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ExportDmabufModule : DesktopModule<ExportDmabufManager>
{
    public override string WireInterface => "zwlr_export_dmabuf_manager_v1";

    public override int Version => ExportDmabufManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IDmabufCapture)];

    protected override ExportDmabufManager Create(BasinServices services) =>
        new(services.Display, services.Find<IDmabufCapture>());
}
