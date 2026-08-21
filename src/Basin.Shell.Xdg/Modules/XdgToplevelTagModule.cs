using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class XdgToplevelTagModule : IProtocolModule
{
    public string WireInterface => "xdg_toplevel_tag_manager_v1";

    public int Version => XdgToplevelTagManager.Version;

    public XdgToplevelTagManager? Manager { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new XdgToplevelTagManager(services.Display);
        services.Use(Manager);
        return Manager;
    }
}
