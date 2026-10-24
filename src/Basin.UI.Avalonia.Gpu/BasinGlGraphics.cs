using Avalonia.OpenGL.Egl;
using Avalonia.Platform;

namespace Basin.UI.Avalonia;

/// <summary>
/// The Avalonia platform graphics over the EGL display of a Basin <see cref="Basin.Render.Gl.GlDevice"/>. Each consumer gets its own context.
/// </summary>
public sealed class BasinGlGraphics : IPlatformGraphics
{
    private readonly EglDisplay _display;
    private readonly List<EglContext> _contexts = [];

    internal BasinGlGraphics(EglDisplay display) => _display = display;

    /// <summary>
    /// Gets a value indicating whether Avalonia uses one shared context. It is always <see langword="false"/>.
    /// </summary>
    public bool UsesSharedContext => false;

    /// <summary>
    /// Creates a new EGL context on the device's display. The owning <see cref="BasinGlGpu"/> disposes it.
    /// </summary>
    /// <returns>The new context.</returns>
    public IPlatformGraphicsContext CreateContext()
    {
        var context = _display.CreateContext(null);
        _contexts.Add(context);
        return context;
    }

    /// <summary>
    /// Always throws, because this provider makes a context per consumer.
    /// </summary>
    /// <returns>No value.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
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
