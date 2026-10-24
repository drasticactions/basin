using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwlr_export_dmabuf_manager_v1</c> global through a <see cref="ExportDmabufManager"/>. It exports frames through <see cref="IDmabufCapture"/> when one is registered.
/// </summary>
public sealed class ExportDmabufModule : DesktopModule<ExportDmabufManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwlr_export_dmabuf_manager_v1";

    /// <inheritdoc/>
    public override int Version => ExportDmabufManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IDmabufCapture)];

    /// <inheritdoc/>
    protected override ExportDmabufManager Create(BasinServices services) =>
        new(services.Display, services.Find<IDmabufCapture>());
}
