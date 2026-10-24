using Avalonia.OpenGL;
using Avalonia.Rendering.Composition;
using Basin.Diagnostics;
using Basin.Hosted;
using static Basin.Avalonia.ViewLog;

namespace Basin.Avalonia;

internal static class ToolkitEglProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static ValueTask<object?> Request(Compositor compositor)
    {
        if (!HostedEglImport.IsSupported)
        {
            return ValueTask.FromResult<object?>(null);
        }

        try
        {
            return compositor.TryGetRenderInterfaceFeature(typeof(IOpenGlTextureSharingRenderInterfaceContextFeature));
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            Log.Warn(error, $"dmabuf: toolkit GPU probe failed, dmabuf waits for the first frame");
            return ValueTask.FromResult<object?>(null);
        }
    }

    public static nint DisplayOf(object? feature)
    {
        if (feature is not IOpenGlTextureSharingRenderInterfaceContextFeature { CanCreateSharedContext: true } sharing)
        {
            return 0;
        }

        try
        {
            using var shared = sharing.CreateSharedContext();
            return shared is global::Avalonia.OpenGL.Egl.EglContext egl ? egl.Display.Handle : 0;
        }
        catch (Exception error) when (error is OpenGlException or InvalidOperationException or NotSupportedException or global::Avalonia.Platform.PlatformGraphicsContextLostException)
        {
            Log.Warn(error, $"dmabuf: toolkit GPU probe failed, dmabuf waits for the first frame");
            return 0;
        }
    }
}
