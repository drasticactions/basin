using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Basin.Render.Skia;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

public sealed class SkiaGlUIHost : IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly GlDevice _device;
    private readonly IAllocator _allocator;
    private readonly GRContext _context;
    private readonly bool _ownsContext;
    private readonly GRGlInterface? _ownInterface;
    private readonly GRGlGetProcedureAddressDelegate? _getProc;
    private bool _disposed;

    public SkiaGlUIHost(GlDevice device, IAllocator allocator, GRContext? sharedContext = null)
    {
        _device = device;
        _allocator = allocator;
        if (sharedContext is not null)
        {
            _context = sharedContext;
            _ownsContext = false;
            return;
        }

        _getProc = GetProcAddress;
        _ownInterface = GRGlInterface.CreateGles(_getProc)
            ?? throw new InvalidOperationException("Ganesh rejected the GLES interface on this context.");
        SkiaCensus.Track(_ownInterface);
        _context = GRContext.CreateGl(_ownInterface)
            ?? throw new InvalidOperationException("Ganesh refused a context on this device.");
        SkiaCensus.Track(_context);
        _ownsContext = true;
    }

    public UITargetKind Produces => UITargetKind.Dmabuf;

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
        if (_disposed || options.Target != UITargetKind.Dmabuf)
        {
            return null;
        }

        var surface = new SkiaGlUISurface(_device, _allocator, _context);
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsContext)
        {
            _context.AbandonContext(releaseResources: true);
            SkiaCensus.Release(_context);
            SkiaCensus.Release(_ownInterface);
        }
    }

    private static nint GetProcAddress(string name)
    {
        var native = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            unsafe
            {
                return (nint)Mesa.Native.Libegl.eglGetProcAddress((sbyte*)native);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(native);
        }
    }
}
