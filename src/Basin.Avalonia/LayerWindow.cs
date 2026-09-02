using Avalonia;
using Avalonia.Controls;

namespace Basin.Avalonia;

public sealed class LayerWindow : Window
{
    private readonly ToplevelWindows _manager;
    private readonly int _id;
    private readonly BasinToplevelView _view;
    private readonly HashSet<uint> _pressedKeys = [];
    private bool _takesKeyboard;
    private HostStackingBand _band;
    private bool _closing;
    private bool _scaleSettled;

    internal LayerWindow(ToplevelWindows manager, int id, bool takesKeyboard, HostStackingBand band)
    {
        _manager = manager;
        _id = id;
        _takesKeyboard = takesKeyboard;
        _band = band;
        _view = new BasinToplevelView(manager.Host, _ => manager.CreateView(id));
        var background = new global::Avalonia.Controls.Panel
        {
            Background = global::Avalonia.Media.Brushes.Transparent,
        };
        background.Children.Add(_view);
        Content = background;
        WindowDecorations = WindowDecorations.None;
        Background = global::Avalonia.Media.Brushes.Transparent;
        TransparencyLevelHint = [global::Avalonia.Controls.WindowTransparencyLevel.Transparent];
        TransparencyBackgroundFallback = global::Avalonia.Media.Brushes.Transparent;
        Topmost = HostStacking.IsTopmost(band);
        ShowInTaskbar = false;
        CanResize = false;
        ShowActivated = takesKeyboard;
        Focusable = takesKeyboard;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Activated += (_, _) =>
        {
            if (!_takesKeyboard)
            {
                return;
            }

            _view.Focus();
            Send(InputKind.FocusIn);
        };
        Deactivated += (_, _) =>
        {
            if (!_takesKeyboard)
            {
                return;
            }

            ReleasePressedKeys();
            Send(InputKind.FocusOut);
        };
        AddHandler(
            global::Avalonia.Input.InputElement.KeyDownEvent,
            (object? _, global::Avalonia.Input.KeyEventArgs e) =>
            {
                ReleaseStaleModifiers(e.KeyModifiers);
                if (e.Key == global::Avalonia.Input.Key.ImeProcessed)
                {
                    return;
                }

                var code = AvaloniaKeyMap.EvdevFor(e.PhysicalKey);
                if (code != 0 && _pressedKeys.Add(code))
                {
                    Send(InputKind.Key, code: code, pressed: true);
                    e.Handled = true;
                }
            },
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(
            global::Avalonia.Input.InputElement.KeyUpEvent,
            (object? _, global::Avalonia.Input.KeyEventArgs e) =>
            {
                var code = AvaloniaKeyMap.EvdevFor(e.PhysicalKey);
                if (code != 0 && _pressedKeys.Remove(code))
                {
                    Send(InputKind.Key, code: code, pressed: false);
                    e.Handled = true;
                }

                ReleaseStaleModifiers(e.KeyModifiers);
            },
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PointerEntered += (_, e) => SendPointer(InputKind.PointerEnter, e);
        PointerMoved += (_, e) => SendPointer(
            e.Pointer.Type == global::Avalonia.Input.PointerType.Touch ? InputKind.TouchMotion : InputKind.PointerMotion, e);
        PointerExited += (_, e) => SendPointer(InputKind.PointerLeave, e);
        PointerPressed += (_, e) =>
        {
            if (e.Pointer.Type == global::Avalonia.Input.PointerType.Touch)
            {
                SendPointer(InputKind.TouchDown, e);
                return;
            }

            ReleaseStaleModifiers(e.KeyModifiers);
            SendPointer(InputKind.PointerMotion, e);
            var button = AvaloniaKeyMap.ButtonFor(e.GetCurrentPoint(_view).Properties.PointerUpdateKind);
            if (button != 0)
            {
                SendPointer(InputKind.PointerButton, e, button, pressed: true);
            }
        };
        PointerReleased += (_, e) =>
        {
            if (e.Pointer.Type == global::Avalonia.Input.PointerType.Touch)
            {
                SendPointer(InputKind.TouchUp, e);
                return;
            }

            var button = AvaloniaKeyMap.ButtonFor(e.GetCurrentPoint(_view).Properties.PointerUpdateKind);
            if (button != 0)
            {
                SendPointer(InputKind.PointerButton, e, button, pressed: false);
            }
        };
        PointerWheelChanged += (_, e) => SendPointer(InputKind.PointerAxis, e, dx: e.Delta.X, dy: e.Delta.Y);
        Opened += (_, _) =>
        {
            HostStacking.Apply(this, _band, _takesKeyboard);
            ObserveScreenScale();
            _manager.HostScaleChanged(_id, RenderScaling, RenderScaling != 1.0);
        };
        ScalingChanged += (_, _) =>
        {
            _scaleSettled = true;
            ObserveScreenScale();
            _manager.HostScaleChanged(_id, RenderScaling, authoritative: true);
        };
        Closing += (_, e) =>
        {
            if (!_closing)
            {
                e.Cancel = true;
                _manager.HostCloseRequested(_id);
            }
        };
    }

    internal BasinToplevelView View => _view;

    internal int Id => _id;

    internal void ApplyCursor(global::Avalonia.Input.Cursor cursor) => Cursor = cursor;

    internal void SetTakesKeyboard(bool takesKeyboard)
    {
        if (_takesKeyboard == takesKeyboard)
        {
            return;
        }

        _takesKeyboard = takesKeyboard;
        ShowActivated = takesKeyboard;
        Focusable = takesKeyboard;
    }

    internal void SetBand(HostStackingBand band)
    {
        if (_band == band)
        {
            return;
        }

        _band = band;
        Topmost = HostStacking.IsTopmost(band);
        HostStacking.Apply(this, band, _takesKeyboard);
    }

    internal void PlaceAt(PixelPoint position, double width, double height)
    {
        Width = width;
        Height = height;
        Position = position;
    }

    private void ObserveScreenScale()
    {
        if (RenderScaling <= 0 || (!_scaleSettled && RenderScaling == 1.0))
        {
            return;
        }

        var screens = Screens;
        var screen = screens.ScreenFromTopLevel(this) ?? screens.ScreenFromWindow(this);
        if (HostScreens.KeyFor(screens, screen) is { } key)
        {
            _manager.HostScreenScaleObserved(key, RenderScaling);
        }
    }

    internal async Task CloseFromCompositorAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        await _view.ShutdownAsync();
        Close();
    }

    private void ReleaseStaleModifiers(global::Avalonia.Input.KeyModifiers held)
    {
        if (AvaloniaKeyMap.StaleModifiers(held, _pressedKeys) is not { } stale)
        {
            return;
        }

        foreach (var code in stale)
        {
            _pressedKeys.Remove(code);
            Send(InputKind.Key, code: code, pressed: false);
        }
    }

    private void ReleasePressedKeys()
    {
        foreach (var code in _pressedKeys)
        {
            Send(InputKind.Key, code: code, pressed: false);
        }

        _pressedKeys.Clear();
    }

    private void Send(InputKind kind, uint code = 0, bool pressed = false) => _manager.Enqueue(new BasinInputEvent
    {
        Kind = kind,
        WindowId = _id,
        TimeMs = (uint)Environment.TickCount,
        Code = code,
        Pressed = pressed,
    });

    private void SendPointer(
        InputKind kind,
        global::Avalonia.Input.PointerEventArgs pointer,
        uint code = 0,
        bool pressed = false,
        double dx = 0,
        double dy = 0)
    {
        var position = pointer.GetPosition(_view);
        _manager.Enqueue(new BasinInputEvent
        {
            Kind = kind,
            WindowId = _id,
            TimeMs = (uint)Environment.TickCount,
            X = position.X,
            Y = position.Y,
            Code = code,
            Pressed = pressed,
            DeltaX = dx,
            DeltaY = dy,
            TouchId = pointer.Pointer.Id,
        });
    }
}
