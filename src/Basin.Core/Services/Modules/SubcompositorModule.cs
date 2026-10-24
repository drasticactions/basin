using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

/// <summary>
/// Installs <c>wl_subcompositor</c>. It requires the <see cref="CompositorGlobal"/>.
/// </summary>
public sealed class SubcompositorModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "wl_subcompositor";

    /// <inheritdoc/>
    public int Version => SubcompositorGlobal.Version;

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new SubcompositorGlobal(services.Display, services.Require<CompositorGlobal>());
    }
}
