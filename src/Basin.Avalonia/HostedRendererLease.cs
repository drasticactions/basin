using Avalonia.Skia;
using Basin.Hosted;

namespace Basin.Avalonia;

public static class HostedRendererLease
{
    public static bool BindFrame(this HostedRenderer renderer, ISkiaSharpApiLease lease)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(lease);
        if (!renderer.BindFrame(lease.SkCanvas, lease.GrContext, lease.CurrentOpacity))
        {
            return false;
        }

        if (renderer.EglImport is null && OperatingSystem.IsLinux() && lease.GrContext is not null)
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
