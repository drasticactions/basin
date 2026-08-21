using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

public sealed class FixesModule : IProtocolModule
{
    public string WireInterface => "wl_fixes";

    public int Version => FixesGlobal.Version;

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new FixesGlobal(services.Display);
    }
}
