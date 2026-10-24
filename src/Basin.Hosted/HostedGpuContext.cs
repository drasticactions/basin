using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Basin.Render.Skia;
using Mesa.Egl;
using Mesa.Gbm;
using Mesa.Native;
using SkiaSharp;

namespace Basin.Hosted;

/// <summary>
/// Owns an offscreen GLES context on a DRM render node and a Skia <see cref="GRContext"/> over it.
/// The context is current on the thread that constructs the object, and only that thread can use it.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed unsafe class HostedGpuContext : IDisposable
{
    private readonly int _deviceFd;
    private readonly GbmDevice _gbm;
    private readonly EglDisplay _display;
    private readonly EglContext _context;
    private readonly GRGlInterface _interface;
    private readonly GRContext _grContext;
    private readonly GRGlGetProcedureAddressDelegate _getProc = GetProcAddress;
    private bool _disposed;

    private HostedGpuContext(string renderNode)
    {
        _deviceFd = HeadlessNative.Open(renderNode, HeadlessNative.OpenReadWrite | HeadlessNative.OpenCloseOnExec);
        if (_deviceFd < 0)
        {
            throw new IOException(
                $"The render node {renderNode} could not be opened. {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}.");
        }

        GbmDevice? gbm = null;
        EglDisplay? display = null;
        EglContext? context = null;
        GRGlInterface? glInterface = null;
        try
        {
            gbm = GbmDevice.Create(_deviceFd);
            display = EglDisplay.GetPlatformDisplay(gbm);
            EglDisplay.BindApi(EglApi.OpenGles);
            Span<int> configAttribs =
            [
                Libegl.EGL_SURFACE_TYPE, 0,
                Libegl.EGL_RENDERABLE_TYPE, 0x40,
                Libegl.EGL_RED_SIZE, 8,
                Libegl.EGL_GREEN_SIZE, 8,
                Libegl.EGL_BLUE_SIZE, 8,
                Libegl.EGL_ALPHA_SIZE, 8,
            ];
            var configs = display.ChooseConfigs(configAttribs);
            Span<int> contextAttribs = [Libegl.EGL_CONTEXT_MAJOR_VERSION, 3];
            context = display.CreateContext(configs[0], null, contextAttribs);
            context.MakeCurrent();
            glInterface = SkiaCensus.Track(GRGlInterface.CreateGles(_getProc)
                ?? throw new InvalidOperationException("Skia rejected the GLES interface of the render node. A GLES 3 driver is necessary."));
            _grContext = SkiaCensus.Track(GRContext.CreateGl(glInterface)
                ?? throw new InvalidOperationException("Skia could not create a GL context on the render node. A GLES 3 driver is necessary."));
        }
        catch
        {
            SkiaCensus.Release(glInterface);
            display?.ReleaseCurrent();
            context?.Dispose();
            display?.Dispose();
            gbm?.Dispose();
            _ = HeadlessNative.Close(_deviceFd);
            throw;
        }

        _gbm = gbm;
        _display = display;
        _context = context;
        _interface = glInterface;
        RenderNode = renderNode;
    }

    /// <summary>
    /// Gets the render node that the context draws on.
    /// </summary>
    public string RenderNode { get; }

    /// <summary>
    /// Gets the Skia context over the GLES context. The object owns it.
    /// </summary>
    public GRContext Context => _grContext;

    /// <summary>
    /// Gets the native EGL display of the render node.
    /// </summary>
    public nint EglDisplayHandle => _display.Handle;

    /// <summary>
    /// Creates the context on a render node and makes it current on the calling thread.
    /// </summary>
    /// <param name="renderNode">The DRM render node, for example <c>/dev/dri/renderD128</c>.</param>
    /// <returns>The context. The caller disposes it on the same thread.</returns>
    /// <exception cref="IOException">The render node cannot be opened.</exception>
    /// <exception cref="EglException">EGL refuses the display, the configuration or the context.</exception>
    /// <exception cref="GbmException">GBM refuses the render node.</exception>
    /// <exception cref="InvalidOperationException">Skia refuses the GLES context.</exception>
    public static HostedGpuContext Create(string renderNode)
    {
        ArgumentException.ThrowIfNullOrEmpty(renderNode);
        return new HostedGpuContext(renderNode);
    }

    /// <summary>
    /// Makes the GLES context current on the calling thread again.
    /// </summary>
    public void MakeCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.MakeCurrent();
    }

    /// <summary>
    /// Abandons the Skia context and releases the GLES context, the EGL display and the render node.
    /// Dispose every texture and surface of the context first.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _context.MakeCurrent();
        _grContext.AbandonContext(releaseResources: true);
        SkiaCensus.Release(_grContext);
        SkiaCensus.Release(_interface);
        _display.ReleaseCurrent();
        _context.Dispose();
        _display.Dispose();
        _gbm.Dispose();
        _ = HeadlessNative.Close(_deviceFd);
    }

    private static nint GetProcAddress(string name)
    {
        var native = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            return (nint)Libegl.eglGetProcAddress((sbyte*)native);
        }
        finally
        {
            Marshal.FreeCoTaskMem(native);
        }
    }
}
