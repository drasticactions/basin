using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class XdgToplevelIconModule : IProtocolModule
{
    public string WireInterface => "xdg_toplevel_icon_manager_v1";

    public int Version => XdgToplevelIconManager.Version;

    public XdgToplevelIconManager? Manager { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new XdgToplevelIconManager(services.Display);
        services.Use(Manager);
        return Manager;
    }
}
