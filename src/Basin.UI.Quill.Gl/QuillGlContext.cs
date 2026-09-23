using System.Runtime.InteropServices;
using Basin.Render.Gl;

namespace Basin.UI.Quill;

internal sealed class QuillGlContext
{
    private const int EglDraw = 0x3059;
    private const int EglRead = 0x305A;

    private readonly nint _display;
    private readonly nint _context;
    private readonly bool _switches;

    internal QuillGlContext(GlDevice device, bool switches)
    {
        _display = device.Egl.Handle;
        _context = device.Context.Handle;
        _switches = switches;
    }

    internal bool Switches => _switches;

    internal Scope Enter()
    {
        if (!_switches)
        {
            return default;
        }

        var current = eglGetCurrentContext();
        if (current == _context)
        {
            return default;
        }

        var previous = current == 0
            ? default
            : new Scope(eglGetCurrentDisplay(), eglGetCurrentSurface(EglDraw), eglGetCurrentSurface(EglRead), current);
        if (eglMakeCurrent(_display, 0, 0, _context) == 0)
        {
            previous.Dispose();
            throw new InvalidOperationException("eglMakeCurrent failed for the quill chrome context.");
        }

        return previous;
    }

    internal static Scope Save()
    {
        var current = eglGetCurrentContext();
        return current == 0
            ? default
            : new Scope(eglGetCurrentDisplay(), eglGetCurrentSurface(EglDraw), eglGetCurrentSurface(EglRead), current);
    }

    [DllImport("libEGL.so.1")]
    private static extern nint eglGetCurrentContext();

    [DllImport("libEGL.so.1")]
    private static extern nint eglGetCurrentDisplay();

    [DllImport("libEGL.so.1")]
    private static extern nint eglGetCurrentSurface(int readDraw);

    [DllImport("libEGL.so.1")]
    private static extern uint eglMakeCurrent(nint display, nint draw, nint read, nint context);

    internal readonly struct Scope : IDisposable
    {
        private readonly nint _display;
        private readonly nint _draw;
        private readonly nint _read;
        private readonly nint _context;

        internal Scope(nint display, nint draw, nint read, nint context)
        {
            _display = display;
            _draw = draw;
            _read = read;
            _context = context;
        }

        public void Dispose()
        {
            if (_context != 0)
            {
                _ = eglMakeCurrent(_display, _draw, _read, _context);
            }
        }
    }
}
