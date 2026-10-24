using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zxdg_exporter_v2</c> global through a <see cref="XdgForeignManager"/>. The manager also advertises the v1 exporter and the importer globals of both versions.
/// </summary>
public sealed class XdgForeignModule : DesktopModule<XdgForeignManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zxdg_exporter_v2";

    /// <inheritdoc/>
    public override int Version => XdgForeignManager.Version;

    /// <inheritdoc/>
    protected override XdgForeignManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
