using SDL3;
using SkiaSharp;

namespace Basin.Sdl;

/// <summary>
/// A render backend for SDL windows. It sets the window flags and creates one <see cref="ISdlRenderLease"/> per window.
/// All leases of one device share one <see cref="GRContext"/>.
/// </summary>
public interface ISdlRenderDevice : IDisposable
{
    /// <summary>
    /// Gets the renderer name that selects this device, one of the <see cref="SdlRenderDevices"/> names.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the flags that a window must have at creation, so that this device can draw into it.
    /// </summary>
    SDL.WindowFlags WindowFlags { get; }

    /// <summary>
    /// Gets the Skia context that all leases of this device draw with.
    /// It is <see langword="null"/> for a raster device and before the first lease creates the context.
    /// </summary>
    GRContext? Context { get; }

    /// <summary>
    /// Gets the EGL display of the GL context, for dmabuf import. It is 0 when the device does not use EGL.
    /// </summary>
    nint EglDisplay { get; }

    /// <summary>
    /// Gets the renderer that imports client dmabufs on the same device as the leases.
    /// It is <see langword="null"/> when the device has no dmabuf import of its own.
    /// </summary>
    IRenderer? DmabufRenderer => null;

    /// <summary>
    /// Gets the DRM render node path of <see cref="DmabufRenderer"/>.
    /// It is <see langword="null"/> when <see cref="DmabufRenderer"/> is <see langword="null"/>.
    /// </summary>
    string? RenderNode => null;

    /// <summary>
    /// Releases a lost context, so that the next <see cref="CreateLease"/> builds a new one.
    /// The caller must dispose every lease of the device first.
    /// </summary>
    /// <returns><see langword="true"/> when the context was lost and is now released, <see langword="false"/> when there was nothing to recover.</returns>
    bool TryRecover() => false;

    /// <summary>
    /// Creates a lease that draws into a window. The first call creates the shared context.
    /// </summary>
    /// <param name="window">A window that was created with <see cref="WindowFlags"/>.</param>
    /// <returns>A new lease that the caller owns and must dispose before the window.</returns>
    ISdlRenderLease CreateLease(SdlWindow window);
}
