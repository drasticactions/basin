using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

public sealed class CompositorModule : IProtocolModule
{
    public string WireInterface => "wl_compositor";

    public int Version => CompositorGlobal.Version;

    public CompositorGlobal? Global { get; private set; }

    public void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.UseDefault(new ClientBufferRegistry());
    }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Global = new CompositorGlobal(services.Display, services.Require<ClientBufferRegistry>());
        services.Use(Global);
        return Global;
    }
}
