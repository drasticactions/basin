using Basin.Avalonia;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class CaptureToggle : IDisposable
{
    private readonly CaptureChord _chord;
    private readonly ToplevelWindows _windows;
    private readonly BasinOutputView _view;
    private readonly BasinCompositorHost _host;
    private readonly Action<bool> _hotkeys;
    private readonly CaptureHooks _hooks;

    private ToplevelWindow? _window;
    private string _title = string.Empty;
    private IDisposable? _grab;
    private long _lastTap;
    private bool _tapIsAlone;
    private HotkeyModifiers _held;
    private bool _disposed;

    public CaptureToggle(
        CaptureChord chord,
        ToplevelWindows windows,
        BasinOutputView view,
        BasinCompositorHost host,
        Action<bool> hotkeys)
    {
        _chord = chord;
        _windows = windows;
        _view = view;
        _host = host;
        _hotkeys = hotkeys;
        _hooks = new CaptureHooks(OnKey, (code, pressed) => _window?.InjectKey(code, pressed));
    }

    public bool Captured { get; private set; }

    public void Attach(ToplevelWindow window, string title)
    {
        ArgumentNullException.ThrowIfNull(window);
        Detach();
        _window = window;
        _title = title;
        _held = HotkeyModifiers.None;
        _lastTap = 0;
        window.KeyFilter = OnKey;
        window.Deactivated += OnDeactivated;
        Log.Info($"the desktop takes this host's keyboard on {_chord.Text}");
    }

    public void Detach()
    {
        if (_window is not { } window)
        {
            return;
        }

        Release();
        window.KeyFilter = null;
        window.Deactivated -= OnDeactivated;
        _window = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
    }

    private void OnDeactivated(object? sender, EventArgs e) => Release();

    private bool OnKey(uint code, bool pressed)
    {
        if (code == _chord.Code)
        {
            if (_chord.DoubleTap)
            {
                if (pressed)
                {
                    _tapIsAlone = true;
                }
                else if (_tapIsAlone && _lastTap != 0 && Environment.TickCount64 - _lastTap <= CaptureChord.DoubleTapMillis)
                {
                    _lastTap = 0;
                    Toggle();
                }
                else
                {
                    _lastTap = Environment.TickCount64;
                }
            }
            else if (pressed && _held == _chord.Modifiers)
            {
                Toggle();
            }

            return true;
        }

        if (CaptureChord.ModifierOf(code) is var modifier && modifier != HotkeyModifiers.None)
        {
            if (pressed)
            {
                _held |= modifier;
            }
            else
            {
                _held &= ~modifier;
            }
        }

        if (pressed)
        {
            _tapIsAlone = false;
        }

        return false;
    }

    private void Toggle()
    {
        if (Captured)
        {
            Release();
            return;
        }

        Captured = true;
        _hotkeys(false);
        _windows.CaptureInput(true);
        _grab = HostCapture.TryGrab(_window!, _view, _host, _hooks);
        _window?.OverrideTitle($"{_title} — captured, {_chord.Text} releases");
        Log.Info($"the desktop has this host's keyboard; {_chord.Text} releases it");
    }

    private void Release()
    {
        if (!Captured)
        {
            return;
        }

        Captured = false;
        _grab?.Dispose();
        _grab = null;
        _windows.CaptureInput(false);
        _hotkeys(true);
        _window?.OverrideTitle(_title);
        Log.Info($"the host has its keyboard back");
    }
}
