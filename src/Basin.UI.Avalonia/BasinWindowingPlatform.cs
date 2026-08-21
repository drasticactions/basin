using Avalonia;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Basin.Capabilities;

namespace Basin.UI.Avalonia;

internal sealed class BasinWindowingPlatform : IWindowingPlatform
{
    private readonly BasinPlatformContext _context;

    public BasinWindowingPlatform(BasinPlatformContext context) => _context = context;

    public IWindowImpl CreateWindow() => new BasinWindowImpl(_context);

    public IWindowImpl CreateEmbeddableWindow() => new BasinWindowImpl(_context);

    public ITopLevelImpl CreateEmbeddableTopLevel() => new BasinWindowImpl(_context);

    public ITrayIconImpl? CreateTrayIcon() => null;

    public void GetWindowsZOrder(ReadOnlySpan<IWindowImpl> windows, Span<long> zOrder)
    {
        for (var i = 0; i < windows.Length; i++)
        {
            zOrder[i] = i;
        }
    }
}
