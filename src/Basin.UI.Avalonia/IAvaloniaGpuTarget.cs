using Avalonia.OpenGL;
using Avalonia.OpenGL.Surfaces;
using Basin.Capabilities;
using Pixman;

namespace Basin.UI.Avalonia;

/// <summary>
/// The GPU buffers of one chrome surface. Avalonia draws into them, and the compositor takes the newest finished buffer.
/// </summary>
public interface IAvaloniaGpuTarget : IDisposable
{
    /// <summary>
    /// Gets the logical size and the scale of the last successful <see cref="Configure"/>.
    /// </summary>
    UISurfaceSize Size { get; }

    /// <summary>
    /// Gets a value indicating whether a frame was finished since the last successful <see cref="Configure"/>.
    /// </summary>
    bool Produced { get; }

    /// <summary>
    /// Gets the damage region to report for a new frame. It is empty, and an empty region means the whole surface. The target owns it.
    /// </summary>
    PixmanRegion32 WholeDamage { get; }

    /// <summary>
    /// Sets the size and scale and allocates a buffer for them. Buffers of the old size are retired.
    /// </summary>
    /// <param name="logicalWidth">The width in logical pixels. It must be positive.</param>
    /// <param name="logicalHeight">The height in logical pixels. It must be positive.</param>
    /// <param name="scale">The scale from logical to buffer pixels. It must be positive.</param>
    /// <returns><see langword="false"/> when a value is not valid, the allocation fails or the target is disposed.</returns>
    bool Configure(int logicalWidth, int logicalHeight, double scale);

    /// <summary>
    /// Takes the newest finished buffer for display.
    /// </summary>
    /// <param name="frame">The frame, with a buffer lock and a fence. The caller must dispose it.</param>
    /// <returns><see langword="false"/> when no frame is finished yet or the target is disposed.</returns>
    bool TryAcquire(out UIFrame frame);

    /// <summary>
    /// Frees buffers that have been idle for some time. The target always keeps one buffer.
    /// </summary>
    /// <param name="nowMillis">The current time in milliseconds on a monotonic clock.</param>
    void Trim(long nowMillis);

    /// <summary>
    /// Creates the Avalonia render target that draws into the buffers of this target.
    /// </summary>
    /// <param name="context">The GL context of the Avalonia compositor. It must be an EGL context.</param>
    /// <param name="onFramePublished">Called after each frame is finished and can be acquired.</param>
    /// <returns>A new render target.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="context"/> is not an EGL context.</exception>
    /// <exception cref="ObjectDisposedException">The target is disposed.</exception>
    IGlPlatformSurfaceRenderTarget CreateRenderTarget(IGlContext context, Action onFramePublished);
}
