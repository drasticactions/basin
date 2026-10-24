using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

/// <summary>
/// Installs <c>wl_fixes</c>.
/// </summary>
public sealed class FixesModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "wl_fixes";

    /// <inheritdoc/>
    public int Version => FixesGlobal.Version;

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new FixesGlobal(services.Display);
    }
}
