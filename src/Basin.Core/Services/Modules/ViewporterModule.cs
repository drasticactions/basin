using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

public sealed class ViewporterModule : IProtocolModule
{
    public string WireInterface => "wp_viewporter";

    public int Version => ViewporterGlobal.Version;

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new ViewporterGlobal(services.Display, services.Require<CompositorGlobal>());
    }
}
