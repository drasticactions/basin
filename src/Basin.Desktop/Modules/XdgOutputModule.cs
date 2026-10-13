using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class XdgOutputModule : DesktopModule<XdgOutputManager>
{
    public override string WireInterface => "zxdg_output_manager_v1";

    public override int Version => XdgOutputManager.Version;

    protected override XdgOutputManager Create(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var manager = new XdgOutputManager(services.Display, services.Require<OutputLayout>());
        services.Use<ILogicalOutputOverrides>(manager);
        return manager;
    }
}
