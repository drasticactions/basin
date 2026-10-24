using Avalonia.Platform;

namespace Basin.UI.Avalonia;

/// <summary>
/// A GPU that Avalonia draws chrome surfaces with. With it, chrome surfaces produce dmabufs instead of memory buffers.
/// </summary>
public interface IAvaloniaGpu
{
    /// <summary>
    /// Gets the platform graphics that the Avalonia compositor renders with.
    /// </summary>
    IPlatformGraphics Graphics { get; }

    /// <summary>
    /// Creates a new render target for one chrome surface. The caller owns the target and must dispose it.
    /// </summary>
    /// <returns>A target with no buffers. It allocates them on the first <see cref="IAvaloniaGpuTarget.Configure"/>.</returns>
    IAvaloniaGpuTarget CreateTarget();
}
