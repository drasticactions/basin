using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class XdgToplevelDragModule : IProtocolModule
{
    public string WireInterface => "xdg_toplevel_drag_manager_v1";

    public int Version => XdgToplevelDragManager.Version;

    public IReadOnlyList<Type> Capabilities => [typeof(IDragTracker)];

    public XdgToplevelDragManager? Manager { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new XdgToplevelDragManager(services.Display, services.Find<IDragTracker>());
        services.Use(Manager);
        return Manager;
    }
}
