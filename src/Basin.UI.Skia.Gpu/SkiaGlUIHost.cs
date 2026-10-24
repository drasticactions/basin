using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Basin.Render.Skia;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

/// <summary>
/// The Skia chrome host on a GL device. It draws <see cref="SkiaGlUISurface"/> instances with Ganesh into dmabufs. Use it on the thread that created it.
/// </summary>
public sealed class SkiaGlUIHost : IUIHost, IUISurfaceObserver, IRendererBound
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<SkiaGlUISurface> _surfaces = [];
    private GlDevice _device;
    private IAllocator _allocator;
    private GRContext? _context;
    private bool _ownsContext;
    private GRGlInterface? _ownInterface;
    private GRGlGetProcedureAddressDelegate? _getProc;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaGlUIHost"/> class. Without a shared context, the host makes a Ganesh context on the current EGL context and owns it.
    /// </summary>
    /// <param name="device">The GL device that the surfaces draw on. The host does not own it.</param>
    /// <param name="allocator">The allocator of the dmabufs. The host does not own it.</param>
    /// <param name="sharedContext">The Ganesh context of a Skia renderer on the same device, or <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">Ganesh rejects the GLES interface or the context.</exception>
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

        _ownsContext = true;
        _context = CreateOwnContext();
    }

    private GRContext Context => _context ??= CreateOwnContext();

    private GRContext CreateOwnContext()
    {
        _getProc ??= GetProcAddress;
        _ownInterface = GRGlInterface.CreateGles(_getProc)
            ?? throw new InvalidOperationException("Ganesh rejected the GLES interface on this context.");
        SkiaCensus.Track(_ownInterface);
        var context = GRContext.CreateGl(_ownInterface)
            ?? throw new InvalidOperationException("Ganesh refused a context on this device.");
        return SkiaCensus.Track(context);
    }

    /// <summary>
    /// Gets <see cref="ISkiaUISurface"/>, which every surface of the host implements.
    /// </summary>
    public Type SurfaceContract => typeof(ISkiaUISurface);

    /// <summary>
    /// Gets <see cref="UITargetKind.Dmabuf"/>, the only kind that the host produces.
    /// </summary>
    public UITargetKind Produces => UITargetKind.Dmabuf;

    /// <summary>
    /// Gets <see langword="null"/>, because the host never has work due.
    /// </summary>
    public long? NextDueMillis => null;

    /// <summary>
    /// Never occurs. The host has no work of its own.
    /// </summary>
    public event Action? WakeupRequested
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>
    /// Creates a configured <see cref="SkiaGlUISurface"/>. The caller owns the surface and must dispose it.
    /// </summary>
    /// <param name="options">The target kind, the logical size and the scale.</param>
    /// <returns>The surface, or <see langword="null"/> when the host is disposed, the kind is not <see cref="UITargetKind.Dmabuf"/>, or the allocation fails.</returns>
    public IUISurface? CreateSurface(in UISurfaceOptions options)
    {
        _thread.Assert();
        if (_disposed || options.Target != UITargetKind.Dmabuf)
        {
            return null;
        }

        var surface = new SkiaGlUISurface(_device, _allocator, () => Context);
        if (!surface.Configure(options.Width, options.Height, options.Scale))
        {
            surface.Dispose();
            return null;
        }

        _surfaces.Add(surface);
        surface.AddObserver(this);
        return surface;
    }

    /// <summary>
    /// Does nothing. The caller draws each surface.
    /// </summary>
    public void Pump()
    {
    }

    /// <summary>
    /// Abandons and releases the Ganesh context when the host owns it. The surfaces that the host made are not disposed.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _surfaces.Clear();
        ReleaseOwnContext();
    }

    /// <inheritdoc/>
    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
    }

    /// <inheritdoc/>
    public void OnSurfaceDestroyed(IUISurface surface)
    {
        if (surface is SkiaGlUISurface glSurface)
        {
            _surfaces.Remove(glSurface);
        }
    }

    /// <summary>
    /// Moves the host and its live surfaces to the device of a new GL or Skia GL renderer. The surfaces keep their buffers and drop their GPU targets. An own Ganesh context is made again on the next draw, when the new context is current.
    /// </summary>
    /// <param name="replacement">The old and the new renderer. The old context must be current.</param>
    /// <exception cref="InvalidOperationException">The new renderer has no GL device.</exception>
    public void OnRendererReplaced(RendererReplacement replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        foreach (var surface in _surfaces)
        {
            surface.ReleaseTargets();
        }

        ReleaseOwnContext();
        _context = null;
        switch (replacement.New.Renderer)
        {
            case SkiaGlRenderer skiaGl:
                _device = skiaGl.Device;
                _context = skiaGl.Context;
                _ownsContext = false;
                break;
            case Basin.Render.Gl.GlRenderer gl:
                _device = gl.Device;
                _ownsContext = true;
                break;
            default:
                throw new InvalidOperationException($"A GL chrome host cannot move to a {replacement.New.Renderer.GetType().Name}.");
        }

        _allocator = _device.CreateAllocator();
        foreach (var surface in _surfaces)
        {
            surface.Rebind(_device, _allocator, () => Context);
        }
    }

    private void ReleaseOwnContext()
    {
        if (!_ownsContext || _context is null)
        {
            return;
        }

        _context.AbandonContext(releaseResources: true);
        SkiaCensus.Release(_context);
        SkiaCensus.Release(_ownInterface);
        _context = null;
        _ownInterface = null;
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
