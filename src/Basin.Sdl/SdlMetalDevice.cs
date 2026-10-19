using Basin.Render.Skia;
using SDL3;
using SkiaSharp;
using static Basin.Sdl.SdlLog;

namespace Basin.Sdl;

public sealed class SdlMetalDevice : ISdlRenderDevice
{
    private nint _device;
    private nint _queue;
    private GRMtlBackendContext? _backend;
    private GRContext? _context;
    private bool _disposed;

    public string Name => SdlRenderDevices.Metal;

    public SDL.WindowFlags WindowFlags => SDL.WindowFlags.Metal;

    public GRContext? Context => _context;

    public nint EglDisplay => 0;

    internal nint Device => _device;

    internal nint Queue => _queue;

    public ISdlRenderLease CreateLease(SdlWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("the Metal lease runs on macOS only");
        }

        if (_context is null)
        {
            Initialize();
        }

        return new SdlMetalLease(this, window);
    }

    private void Initialize()
    {
        _device = SdlObjC.MTLCreateSystemDefaultDevice();
        if (_device == 0)
        {
            throw new InvalidOperationException("MTLCreateSystemDefaultDevice returned no device");
        }

        _queue = SdlObjC.Send(_device, SdlObjC.Selector("newCommandQueue"));
        _backend = new GRMtlBackendContext { DeviceHandle = _device, QueueHandle = _queue };
        _context = GRContext.CreateMetal(_backend) ??
            throw new InvalidOperationException("Ganesh could not create a Metal context.");
        SkiaCensus.Track(_context);
        Log.Info($"Metal lease on the system default device");
    }

    public bool TryRecover()
    {
        if (_disposed || _context is not { IsAbandoned: true })
        {
            return false;
        }

        Release();
        return true;
    }

    private void Release()
    {
        if (_context is { } context)
        {
            context.AbandonContext(false);
            SkiaCensus.Release(context);
            _context = null;
        }

        _backend?.Dispose();
        _backend = null;
        var release = SdlObjC.Selector("release");
        if (_queue != 0)
        {
            SdlObjC.Send(_queue, release);
            _queue = 0;
        }

        if (_device != 0)
        {
            SdlObjC.Send(_device, release);
            _device = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (OperatingSystem.IsMacOS())
        {
            Release();
        }
    }
}
