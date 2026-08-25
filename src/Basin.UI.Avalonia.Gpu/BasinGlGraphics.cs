using Avalonia.OpenGL.Egl;
using Avalonia.Platform;

namespace Basin.UI.Avalonia;

public sealed class BasinGlGraphics : IPlatformGraphics
{
    private readonly EglDisplay _display;
    private readonly List<EglContext> _contexts = [];

    internal BasinGlGraphics(EglDisplay display) => _display = display;

    public bool UsesSharedContext => false;

    public IPlatformGraphicsContext CreateContext()
    {
        var context = _display.CreateContext(null);
        _contexts.Add(context);
        return context;
    }

    public IPlatformGraphicsContext GetSharedContext() =>
        throw new NotSupportedException("This graphics provider hands out a context per consumer.");

    internal void DisposeContexts()
    {
        foreach (var context in _contexts.ToArray())
        {
            context.Dispose();
        }

        _contexts.Clear();
    }
}
