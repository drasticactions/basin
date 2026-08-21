using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Avalonia;

public sealed class PopupWindow
{
    private readonly ToplevelWindows _manager;
    private readonly int _id;
    private readonly BasinToplevelView _view;
    private readonly global::Avalonia.Controls.Panel _content;
    private readonly global::Avalonia.Controls.Primitives.Popup _popup;
    private bool _closing;

    internal PopupWindow(ToplevelWindows manager, int id)
    {
        _manager = manager;
        _id = id;
        _view = new BasinToplevelView(manager.Host, _ => manager.CreateView(id));
        _content = new global::Avalonia.Controls.Panel
        {
            Background = global::Avalonia.Media.Brushes.Transparent,
        };
        _content.Children.Add(_view);
        _popup = new global::Avalonia.Controls.Primitives.Popup
        {
            Child = _content,
            Placement = global::Avalonia.Controls.PlacementMode.AnchorAndGravity,
            PlacementAnchor = global::Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = global::Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
            IsLightDismissEnabled = false,
            Topmost = true,
            WindowManagerAddShadowHint = false,
        };

        _popup.Opened += (_, _) =>
        {
            if (global::Avalonia.Controls.TopLevel.GetTopLevel(_content) is { } top)
            {
                top.TransparencyLevelHint = [global::Avalonia.Controls.WindowTransparencyLevel.Transparent];
                top.TransparencyBackgroundFallback = global::Avalonia.Media.Brushes.Transparent;
                if (top is global::Avalonia.Controls.ContentControl root)
                {
                    root.Background = null;
                }

                _manager.HostScaleChanged(_id, top.RenderScaling, top.RenderScaling != 1.0);
            }
        };

        _content.PointerEntered += (_, e) => SendPointer(InputKind.PointerEnter, e);
        _content.PointerMoved += (_, e) => SendPointer(
            e.Pointer.Type == global::Avalonia.Input.PointerType.Touch ? InputKind.TouchMotion : InputKind.PointerMotion, e);
        _content.PointerExited += (_, e) => SendPointer(InputKind.PointerLeave, e);
        _content.PointerPressed += (_, e) =>
        {
            if (e.Pointer.Type == global::Avalonia.Input.PointerType.Touch)
            {
                SendPointer(InputKind.TouchDown, e);
                return;
            }

            SendPointer(InputKind.PointerMotion, e);
            var button = AvaloniaKeyMap.ButtonFor(e.GetCurrentPoint(_view).Properties.PointerUpdateKind);
            if (button != 0)
            {
                SendPointer(InputKind.PointerButton, e, button, pressed: true);
            }
        };
        _content.PointerReleased += (_, e) =>
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
        _content.PointerWheelChanged += (_, e) => SendPointer(InputKind.PointerAxis, e, dx: e.Delta.X, dy: e.Delta.Y);
    }

    internal BasinToplevelView View => _view;

    internal bool IsOpen => _popup.IsOpen;

    internal void ApplyCursor(global::Avalonia.Input.Cursor cursor) => _content.Cursor = cursor;

    internal void SetContentSize(double width, double height)
    {
        _content.Width = width;
        _content.Height = height;
    }

    internal void PlaceAt(global::Avalonia.Controls.Control target, double x, double y)
    {
        _popup.PlacementTarget = target;
        _popup.PlacementRect = new global::Avalonia.Rect(x, y, 1, 1);
        if (!_popup.IsOpen && !_closing)
        {
            _popup.Open();
        }
    }

    internal void Hide() => _popup.Close();

    internal void RequeryIme() => _view.RaiseEvent(new global::Avalonia.Input.TextInput.TextInputMethodClientRequeryRequestedEventArgs
    {
        RoutedEvent = global::Avalonia.Input.InputMethod.TextInputMethodClientRequeryRequestedEvent,
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

    internal async Task CloseFromCompositorAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        await _view.ShutdownAsync();
        _popup.Close();
    }
}
