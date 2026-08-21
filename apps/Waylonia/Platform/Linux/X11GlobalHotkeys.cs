using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.XWayland;

namespace Waylonia;

internal sealed class X11GlobalHotkeys : IDisposable
{
    private readonly BasinOutputView _view;

    private X11HotkeyGrabber? _grabber;

    private X11GlobalHotkeys(BasinOutputView view) => _view = view;

    public static X11GlobalHotkeys Start(
        IReadOnlyList<Hotkey> hotkeys, BasinOutputView view, BasinCompositorHost host, Action<Hotkey> launch)
    {
        var instance = new X11GlobalHotkeys(view);
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        view.Post(() =>
        {
            var grabber = X11HotkeyGrabber.TryConnect(host.Loop, display);
            if (grabber is null)
            {
                BasinLog.Warn($"global hotkeys are off");
                return;
            }

            var registered = 0;
            foreach (var hotkey in hotkeys)
            {
                var captured = hotkey;
                if (grabber.TryGrab(
                        Modifiers(hotkey.Modifiers), hotkey.Key,
                        () => Dispatcher.UIThread.Post(() => launch(captured))))
                {
                    registered++;
                }
                else
                {
                    BasinLog.Warn($"hotkey '{hotkey.Chord}' was refused, skipping");
                }
            }

            if (registered == 0)
            {
                grabber.Dispose();
                return;
            }

            instance._grabber = grabber;
            BasinLog.Debug($"{registered} global hotkey(s) registered");
        });
        return instance;
    }

    public void Dispose() => _view.Post(() =>
    {
        _grabber?.Dispose();
        _grabber = null;
    });

    private static X11HotkeyModifiers Modifiers(HotkeyModifiers modifiers)
    {
        var value = X11HotkeyModifiers.None;
        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            value |= X11HotkeyModifiers.Shift;
        }

        if ((modifiers & HotkeyModifiers.Ctrl) != 0)
        {
            value |= X11HotkeyModifiers.Ctrl;
        }

        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            value |= X11HotkeyModifiers.Alt;
        }

        if ((modifiers & HotkeyModifiers.Super) != 0)
        {
            value |= X11HotkeyModifiers.Super;
        }

        return value;
    }
}
