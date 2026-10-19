using Basin.Render.Skia;
using SDL3;
using SkiaSharp;
using static Basin.Sdl.SdlLog;

namespace Basin.Sdl;

public sealed class SdlGlDevice : ISdlRenderDevice
{
    private readonly GRGlGetProcedureAddressDelegate _getProc;
    private GRGlInterface? _interface;
    private GRContext? _context;
    private nint _glContext;
    private bool _disposed;

    public SdlGlDevice()
    {
        _getProc = name => SDL.GLGetProcAddress(name);
        if (OperatingSystem.IsLinux())
        {
            SDL.SetHint(SDL.Hints.VideoForceEGL, "1");
        }

        if (!SDL.GLLoadLibrary(null))
        {
            throw new InvalidOperationException($"SDL_GL_LoadLibrary failed: {SDL.GetError()}");
        }

        _ = SDL.GLSetAttribute(SDL.GLAttr.RedSize, 8);
        _ = SDL.GLSetAttribute(SDL.GLAttr.GreenSize, 8);
        _ = SDL.GLSetAttribute(SDL.GLAttr.BlueSize, 8);
        _ = SDL.GLSetAttribute(SDL.GLAttr.AlphaSize, 8);
        _ = SDL.GLSetAttribute(SDL.GLAttr.DepthSize, 0);
        _ = SDL.GLSetAttribute(SDL.GLAttr.StencilSize, 8);
        _ = SDL.GLSetAttribute(SDL.GLAttr.DoubleBuffer, 1);
        if (OperatingSystem.IsMacOS())
        {
            _ = SDL.GLSetAttribute(SDL.GLAttr.ContextProfileMask, (int)SDL.GLProfile.Core);
            _ = SDL.GLSetAttribute(SDL.GLAttr.ContextMajorVersion, 4);
            _ = SDL.GLSetAttribute(SDL.GLAttr.ContextMinorVersion, 1);
        }
    }

    public string Name => SdlRenderDevices.Gl;

    public SDL.WindowFlags WindowFlags => SDL.WindowFlags.OpenGL;

    public GRContext? Context => _context;

    public nint GlContext => _glContext;

    public nint EglDisplay { get; private set; }

    public bool HasBufferAge { get; private set; }

    public ISdlRenderLease CreateLease(SdlWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_glContext == 0)
        {
            CreateContext(window);
        }

        return new SdlGlLease(this, window);
    }

    private void CreateContext(SdlWindow window)
    {
        _glContext = SDL.GLCreateContext(window.Handle);
        if (_glContext == 0)
        {
            throw new InvalidOperationException($"SDL_GL_CreateContext failed: {SDL.GetError()}");
        }

        _ = SDL.GLMakeCurrent(window.Handle, _glContext);
        _ = SDL.GLSetSwapInterval(1);
        _interface = GRGlInterface.Create(_getProc);
        if (_interface is null)
        {
            DestroyContext();
            throw new InvalidOperationException("Ganesh rejected the GL interface of the SDL context.");
        }

        SkiaCensus.Track(_interface);
        _context = GRContext.CreateGl(_interface);
        if (_context is null)
        {
            DestroyContext();
            throw new InvalidOperationException("Ganesh could not create a context over the SDL GL context.");
        }

        SkiaCensus.Track(_context);
        if (OperatingSystem.IsLinux())
        {
            EglDisplay = SDL.EGLGetCurrentDisplay();
            HasBufferAge = SdlEgl.HasBufferAge(EglDisplay);
        }

        Log.Info($"GL lease on {SDL.GetCurrentVideoDriver()}: egl={(EglDisplay != 0 ? "yes" : "no")} buffer-age={(HasBufferAge ? "yes" : "no")}");
    }

    internal bool MakeCurrent(SdlWindow window) => SDL.GLMakeCurrent(window.Handle, _glContext);

    private void DestroyContext()
    {
        if (_context is { } context)
        {
            context.AbandonContext(false);
            SkiaCensus.Release(context);
            _context = null;
        }

        if (_interface is { } glInterface)
        {
            SkiaCensus.Release(glInterface);
            _interface = null;
        }

        if (_glContext != 0)
        {
            _ = SDL.GLDestroyContext(_glContext);
            _glContext = 0;
        }

        EglDisplay = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyContext();
        SDL.GLUnloadLibrary();
    }
}
