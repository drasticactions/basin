using System.Runtime.CompilerServices;
using Avalonia.Skia;
using Basin.Diagnostics;
using Basin.Hosted;
using SkiaSharp;
using static Basin.Avalonia.ViewLog;

namespace Basin.Avalonia;

/// <summary>
/// Binds a <see cref="HostedRenderer"/> to the canvas of an Avalonia Skia lease.
/// </summary>
public static class HostedRendererLease
{
    private static readonly ConditionalWeakTable<HostedRenderer, GRContext> Probed = new();

    /// <summary>
    /// Binds the renderer to the canvas, GPU context and opacity of the lease.
    /// On the first frame of each GPU context on an Avalonia EGL context, it also makes that context the offscreen context of the renderer and enables EGL dmabuf import through its display.
    /// A new EGL display replaces the import of the previous one.
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

        if (HostedEglImport.IsSupported && lease.GrContext is { } context &&
            (!Probed.TryGetValue(renderer, out var probed) || !ReferenceEquals(probed, context)))
        {
            Probed.AddOrUpdate(renderer, context);
            Probe(renderer, lease, context);
        }

        return true;
    }

    private static void Probe(HostedRenderer renderer, ISkiaSharpApiLease lease, GRContext context)
    {
        using var platform = lease.TryLeasePlatformGraphicsApi();
        if (platform?.Context is not global::Avalonia.OpenGL.Egl.EglContext egl)
        {
            if (renderer.OffscreenBinding is AvaloniaContextBinding)
            {
                renderer.UseOffscreenContext(null);
            }

            Log.Info($"dmabuf: toolkit GPU context is not EGL, host offers no EGL dmabuf import");
            return;
        }

        renderer.UseOffscreenContext(context, new AvaloniaContextBinding(egl));
        renderer.TryEnableEgl(egl.Display.Handle);
    }
}
