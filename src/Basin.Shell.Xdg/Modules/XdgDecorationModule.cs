using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class XdgDecorationModule : IProtocolModule
{
    public string WireInterface => "zxdg_decoration_manager_v1";

    public int Version => XdgDecorationManager.Version;

    public IReadOnlyList<Type> Capabilities => [typeof(IFrameRenderer)];

    public XdgDecorationManager? Manager { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new XdgDecorationManager(services.Display)
        {
            DefaultMode = services.Find<IFrameRenderer>() is null
                ? DecorationMode.ClientSide
                : DecorationMode.ServerSide,
        };
        services.Use(Manager);
        return Manager;
    }
}
