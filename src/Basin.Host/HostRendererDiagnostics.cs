using Basin.Capabilities;

namespace Basin.Host;

/// <summary>
/// Reads the renderer of the host's <see cref="OutputDriver"/>. Until a driver exists, it reports no renderer.
/// </summary>
/// <param name="host">The host whose driver is read.</param>
public sealed class HostRendererDiagnostics(BasinHost host) : IRendererDiagnostics
{
    /// <inheritdoc/>
    public string? RendererName => host.Driver?.Renderer.GetType().Name;

    /// <inheritdoc/>
    public RendererLossDetection LossDetection => host.Driver?.Renderer.LossDetection ?? RendererLossDetection.Never;

    /// <inheritdoc/>
    public long Rebuilds => host.Driver?.RendererRebuilds ?? 0;
}
