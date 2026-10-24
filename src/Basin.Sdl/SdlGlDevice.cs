using Basin.Render.Skia;
using SDL3;
using SkiaSharp;
using static Basin.Sdl.SdlLog;

namespace Basin.Sdl;

/// <summary>
/// The render device for the <see cref="SdlRenderDevices.Gl"/> name. One GL context and one Skia context serve every window.
/// The device holds the SDL GL library for its whole life.
/// </summary>
public sealed class SdlGlDevice : ISdlRenderDevice
{
    private readonly GRGlGetProcedureAddressDelegate _getProc;
    private GRGlInterface? _interface;
    private GRContext? _context;
    private nint _glContext;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlGlDevice"/> class. It loads the GL library and sets the context attributes.
    /// On Linux it forces EGL. The constructor must run before the first GL window is created.
    /// </summary>
    /// <exception cref="InvalidOperationException">SDL cannot load the GL library.</exception>
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

    /// <inheritdoc/>
    public string Name => SdlRenderDevices.Gl;

    /// <inheritdoc/>
    public SDL.WindowFlags WindowFlags => SDL.WindowFlags.OpenGL;

    /// <inheritdoc/>
    public GRContext? Context => _context;

    /// <summary>
    /// Gets the SDL GL context handle. It is 0 before the first lease.
    /// </summary>
    public nint GlContext => _glContext;

    /// <summary>
    /// Gets the EGL display of the GL context. It is 0 before the first lease and outside Linux.
    /// </summary>
    public nint EglDisplay { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the EGL display offers <c>EGL_EXT_buffer_age</c>. Without it, every frame has age 0.
    /// </summary>
    public bool HasBufferAge { get; private set; }

    /// <summary>
    /// Creates a GL lease for a window. The first call creates the GL context and the Skia context on that window.
    /// </summary>
    /// <param name="window">A window that was created with <see cref="WindowFlags"/>.</param>
    /// <returns>A new lease that the caller owns and must dispose before the window.</returns>
    /// <exception cref="InvalidOperationException">SDL or Skia cannot create the context.</exception>
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

    /// <summary>
    /// Destroys the contexts and releases the GL library. A second call does nothing.
    /// </summary>
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
