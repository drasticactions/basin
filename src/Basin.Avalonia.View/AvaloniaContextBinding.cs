using Avalonia.OpenGL;
using Avalonia.Platform;
using Basin.Diagnostics;
using Basin.Hosted;
using static Basin.Avalonia.ViewLog;

namespace Basin.Avalonia;

internal sealed class AvaloniaContextBinding : IHostedContextBinding
{
    private readonly IPlatformGraphicsContext _context;
    private IDisposable? _restore;

    public AvaloniaContextBinding(IPlatformGraphicsContext context) => _context = context;

    public IPlatformGraphicsContext Context => _context;

    public bool Enter()
    {
        if (_context.IsLost)
        {
            return false;
        }

        try
        {
            _restore = _context.EnsureCurrent();
            return true;
        }
        catch (Exception error) when (error is PlatformGraphicsContextLostException or OpenGlException)
        {
            Log.Debug(error, $"capture: toolkit GPU context did not become current");
            return false;
        }
    }

    public void Exit()
    {
        var restore = _restore;
        _restore = null;
        restore?.Dispose();
    }
}
