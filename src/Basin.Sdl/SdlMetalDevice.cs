using Basin.Render.Skia;
using SDL3;
using SkiaSharp;
using static Basin.Sdl.SdlLog;

namespace Basin.Sdl;

/// <summary>
/// The render device for the <see cref="SdlRenderDevices.Metal"/> name. It uses the system default Metal device and one command queue.
/// It can create leases on macOS only.
/// </summary>
public sealed class SdlMetalDevice : ISdlRenderDevice
{
    private nint _device;
    private nint _queue;
    private GRMtlBackendContext? _backend;
    private GRContext? _context;
    private bool _disposed;

    /// <inheritdoc/>
    public string Name => SdlRenderDevices.Metal;

    /// <inheritdoc/>
    public SDL.WindowFlags WindowFlags => SDL.WindowFlags.Metal;

    /// <inheritdoc/>
    public GRContext? Context => _context;

    /// <inheritdoc/>
    public nint EglDisplay => 0;

    internal nint Device => _device;

    internal nint Queue => _queue;

    /// <summary>
    /// Creates a Metal lease for a window. The first call creates the Metal device, the queue and the Skia context.
    /// </summary>
    /// <param name="window">A window that was created with <see cref="WindowFlags"/>.</param>
    /// <returns>A new lease that the caller owns and must dispose before the window.</returns>
    /// <exception cref="PlatformNotSupportedException">The operating system is not macOS.</exception>
    /// <exception cref="InvalidOperationException">Metal or Skia cannot create the device or the context.</exception>
    public ISdlRenderLease CreateLease(SdlWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("The Metal lease runs on macOS only.");
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
            throw new InvalidOperationException("MTLCreateSystemDefaultDevice returned no device.");
        }

        _queue = SdlObjC.Send(_device, SdlObjC.Selector("newCommandQueue"));
        _backend = new GRMtlBackendContext { DeviceHandle = _device, QueueHandle = _queue };
        _context = GRContext.CreateMetal(_backend) ??
            throw new InvalidOperationException("Ganesh could not create a Metal context.");
        SkiaCensus.Track(_context);
        Log.Info($"metal: lease on the system default device");
    }

    /// <inheritdoc/>
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

    /// <summary>
    /// Releases the Skia context, the queue and the Metal device. A second call does nothing.
    /// </summary>
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
