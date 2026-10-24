using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zxdg_output_manager_v1</c> global through a <see cref="XdgOutputManager"/>.
/// </summary>
public sealed class XdgOutputModule : DesktopModule<XdgOutputManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zxdg_output_manager_v1";

    /// <inheritdoc/>
    public override int Version => XdgOutputManager.Version;

    /// <inheritdoc/>
    protected override XdgOutputManager Create(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var manager = new XdgOutputManager(services.Display, services.Require<OutputLayout>());
        services.Use<ILogicalOutputOverrides>(manager);
        return manager;
    }
}
