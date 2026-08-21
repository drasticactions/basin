using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

public sealed class SubcompositorModule : IProtocolModule
{
    public string WireInterface => "wl_subcompositor";

    public int Version => SubcompositorGlobal.Version;

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new SubcompositorGlobal(services.Display, services.Require<CompositorGlobal>());
    }
}
