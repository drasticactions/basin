using Avalonia.OpenGL;
using Avalonia.OpenGL.Surfaces;
using Basin.Capabilities;
using Pixman;

namespace Basin.UI.Avalonia;

public interface IAvaloniaGpuTarget : IDisposable
{
    UISurfaceSize Size { get; }

    bool Produced { get; }

    PixmanRegion32 WholeDamage { get; }

    bool Configure(int logicalWidth, int logicalHeight, double scale);

    bool TryAcquire(out UIFrame frame);

    void Trim(long nowMillis);

    IGlPlatformSurfaceRenderTarget CreateRenderTarget(IGlContext context, Action onFramePublished);
}
