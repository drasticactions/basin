using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.UI.Skia;

public sealed class SkiaUIHost : IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private bool _disposed;

    public UITargetKind Produces => UITargetKind.Memory;

    public long? NextDueMillis => null;

    public event Action? WakeupRequested
    {
        add
        {
        }

        remove
        {
        }
    }

    public IUISurface? CreateSurface(in UISurfaceOptions options)
    {
        _thread.Assert();
        if (_disposed || options.Target != UITargetKind.Memory)
        {
            return null;
        }

        var surface = new SkiaUISurface();
        if (!surface.Configure(options.Width, options.Height, options.Scale))
        {
            surface.Dispose();
            return null;
        }

        return surface;
    }

    public void Pump()
    {
    }

    public void Dispose()
    {
        _thread.Assert();
        _disposed = true;
    }
}
