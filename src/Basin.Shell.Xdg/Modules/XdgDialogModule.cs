using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class XdgDialogModule : IProtocolModule
{
    public string WireInterface => "xdg_wm_dialog_v1";

    public int Version => XdgDialogManager.Version;

    public XdgDialogManager? Manager { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new XdgDialogManager(services.Display);
        services.Use(Manager);
        return Manager;
    }
}
