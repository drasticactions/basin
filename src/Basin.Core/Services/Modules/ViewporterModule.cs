using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

/// <summary>
/// Installs <c>wp_viewporter</c>. It requires the <see cref="CompositorGlobal"/>.
/// </summary>
public sealed class ViewporterModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "wp_viewporter";

    /// <inheritdoc/>
    public int Version => ViewporterGlobal.Version;

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new ViewporterGlobal(services.Display, services.Require<CompositorGlobal>());
    }
}
