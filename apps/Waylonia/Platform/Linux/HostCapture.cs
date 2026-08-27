using Basin.Avalonia;
using Basin.XWayland;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class HostCapture : IDisposable
{
    private readonly BasinOutputView _view;
    private X11KeyboardGrab? _grab;
    private bool _disposed;

    private HostCapture(BasinOutputView view) => _view = view;

    public static IDisposable? TryGrab(
        global::Avalonia.Controls.TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        CaptureHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(hooks);
        var display = HostSession.Display;
        if (!string.IsNullOrEmpty(HostSession.WaylandDisplay) || string.IsNullOrEmpty(display))
        {
            Log.Warn($"a Wayland host keeps its own chords: no client protocol inhibits them, " +
                $"so Super and the host's bindings stay with it while the desktop is captured");
            return null;
        }

        var instance = new HostCapture(view);
        view.Post(() =>
        {
            if (instance._disposed)
            {
                return;
            }

            instance._grab = X11KeyboardGrab.TryGrab(host.Loop, display, (code, pressed) =>
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!hooks.Filter(code, pressed))
                    {
                        hooks.Inject(code, pressed);
                    }
                }));
            if (instance._grab is null)
            {
                Log.Warn($"the host's chords stay with it while the desktop is captured");
            }
            else
            {
                Log.Debug($"the X keyboard is grabbed on {display}; every key goes to the desktop");
            }
        });
        return instance;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.Post(() =>
        {
            _grab?.Dispose();
            _grab = null;
        });
    }
}
