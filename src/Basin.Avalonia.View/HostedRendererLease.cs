using Avalonia.Skia;
using Basin.Hosted;

namespace Basin.Avalonia;

/// <summary>
/// Binds a <see cref="HostedRenderer"/> to the canvas of an Avalonia Skia lease.
/// </summary>
public static class HostedRendererLease
{
    /// <summary>
    /// Binds the renderer to the canvas, GPU context and opacity of the lease.
    /// On the first GPU frame it also tries to enable EGL dmabuf import through the Avalonia EGL display.
    /// </summary>
    /// <param name="renderer">The renderer to bind.</param>
    /// <param name="lease">The Skia lease of the current Avalonia frame. It must stay alive until the renderer is unbound.</param>
    /// <returns><see langword="false"/> when the GPU context is abandoned and nothing is bound.</returns>
    public static bool BindFrame(this HostedRenderer renderer, ISkiaSharpApiLease lease)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(lease);
        if (!renderer.BindFrame(lease.SkCanvas, lease.GrContext, lease.CurrentOpacity))
        {
            return false;
        }

        if (renderer.EglImport is null && HostedEglImport.IsSupported && lease.GrContext is not null)
        {
            using var platform = lease.TryLeasePlatformGraphicsApi();
            if (platform?.Context is global::Avalonia.OpenGL.Egl.EglContext egl)
            {
                renderer.TryEnableEgl(egl.Display.Handle);
            }
        }

        return true;
    }
}
