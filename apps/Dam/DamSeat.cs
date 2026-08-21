using Basin;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Basin.Host;
using Basin.Scene;
using Basin.Seat;
using Basin.Seat.Backends;
using Microsoft.Extensions.Logging;
using Xkb;

namespace Dam;

internal sealed class DamSeat : IDisposable
{
    private const uint BtnLeft = 0x110;

    private static readonly XkbKeysym Escape = XkbKeysym.FromName("Escape");
    private static readonly XkbKeysym SwitchVt1 = XkbKeysym.FromName("XF86Switch_VT_1");
    private static readonly XkbKeysym SwitchVt12 = XkbKeysym.FromName("XF86Switch_VT_12");

    private readonly Basin.Host.BasinHost _host;
    private readonly Basin.Seat.Seat _seat;
    private readonly DamViews _views;
    private readonly OutputDriver _outputs;
    private readonly Scene _scene;
    private readonly OutputLayout _layout;
    private readonly LayoutPointer _pointer;
    private readonly CursorController _cursor;
    private readonly CursorImageTheme _cursorTheme;
    private readonly SeatIdleSource _idle;
    private readonly RelativePointerManager _relativePointer;
    private readonly TouchPoints _touchPoints = new();
    private readonly Action _stop;
    private readonly bool _allowVtSwitch;
    private readonly ILogger _log;

    private readonly SeatBinder _binder;

    private LibinputBackend? _libinput;
    private int _livePoints;
    private int _touchId = -1;
    private double _touchX;
    private double _touchY;
    private SceneSurface? _dragIcon;

    public DamSeat(
        Basin.Host.BasinHost host,
        BasinServices services,
        DamViews views,
        OutputDriver outputs,
        Scene scene,
        OutputLayout layout,
        CursorImageTheme cursorTheme,
        DamInputSink inputSink,
        SeatIdleSource idle,
        bool allowVtSwitch,
        Action stop,
        ILogger log)
    {
        _host = host;
        _seat = services.Require<Basin.Seat.Seat>();
        _views = views;
        _outputs = outputs;
        _scene = scene;
        _layout = layout;
        _cursorTheme = cursorTheme;
        _idle = idle;
        _allowVtSwitch = allowVtSwitch;
        _stop = stop;
        _log = log;

        _pointer = new LayoutPointer(layout);
        _cursor = new CursorController(layout)
        {
            Shapes = services.Require<CursorShapeManager>(),
            Capture = services.Find<Basin.Capabilities.IScreenCapture>(),
        };
        _cursor.Shapes.CursorRequested += _cursor.ShowImage;
        _relativePointer = services.Require<RelativePointerManager>();
        _binder = new SeatBinder(_seat, layout, _pointer, _cursor)
        {
            Drm = host.Drm,
            Theme = cursorTheme,
        };
        _binder.Key += OnKey;
        _binder.ModifiersChanged += _idle.NotifyActivity;
        _binder.Motion += ProcessCursorMotion;
        _binder.Button += OnButton;
        _binder.Axis += OnAxis;
        _binder.PointerLeft += _seat.Pointer.NotifyClearFocus;
        _binder.TouchDown += OnTouchDown;
        _binder.TouchMotion += OnTouchMotion;
        _binder.TouchUp += OnTouchUp;
        _binder.TouchFrame += OnTouchFrame;
        _binder.TouchCancelled += OnTouchCancel;

        _seat.Capabilities = SeatCapability.None;
        _seat.Keyboard.SetKeymap(SystemKeymap.Read());
        _seat.Keyboard.SetRepeatInfo(25, 600);
        _seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;

        _views.PointerRefocus = () => ProcessCursorMotion((uint)Environment.TickCount);

        _seat.DataDevice.DragStarted += drag =>
        {
            if (drag.Icon is { } icon)
            {
                _dragIcon = new SceneSurface(_scene.Root, icon);
                PositionDragIcon();
            }
        };
        _seat.DataDevice.DragEnded += () =>
        {
            if (_dragIcon is { IsDestroyed: false } icon)
            {
                icon.Destroy();
            }

            _dragIcon = null;
        };

        _outputs.Added += view => _cursor.AddOutput(view.Output, view.Scene);
        _outputs.Removed += view => _cursor.RemoveOutput(view.Output);

        inputSink.OnKey = (keyboard, timeMs, key, pressed) =>
        {
            _seat.Keyboard.Activate(keyboard);
            OnKey(timeMs, key, pressed);
            return true;
        };
        inputSink.OnKeyboardCreated = () => _seat.SetCapability(SeatCapability.Keyboard, true);
        inputSink.OnPointerMotion = (timeMs, dx, dy) =>
        {
            _binder.EnsurePointerCapability();
            _pointer.Motion(dx, dy);
            ProcessCursorMotion(timeMs, dx, dy);
            return true;
        };
        inputSink.OnPointerMotionAbsolute = (timeMs, x, y, extentWidth, extentHeight) =>
        {
            _binder.EnsurePointerCapability();
            var bounds = _layout.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return true;
            }

            var previousX = _pointer.X;
            var previousY = _pointer.Y;
            _pointer.Warp(
                bounds.X + (x / extentWidth * bounds.Width),
                bounds.Y + (y / extentHeight * bounds.Height));
            ProcessCursorMotion(timeMs, _pointer.X - previousX, _pointer.Y - previousY);
            return true;
        };
        inputSink.OnPointerButton = (timeMs, button, pressed) =>
        {
            OnButton(timeMs, button, pressed);
            return true;
        };

        if (_host.Drm is not null)
        {
            WireLibinput();
        }
        else if (_host.Parent is { } parent)
        {
            _binder.BindParent(parent);
        }
    }

    internal void Warp(double x, double y)
    {
        _pointer.Warp(x, y);
        ProcessCursorMotion((uint)Environment.TickCount);
    }

    public void CenterCursor()
    {
        var bounds = _layout.Bounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        _pointer.Warp(bounds.Width / 2.0, bounds.Height / 2.0);
        _cursor.MoveTo(_pointer.X, _pointer.Y);
    }

    private void WireLibinput()
    {
        var libinput = new LibinputBackend(_host.Loop, _host.Session!);
        _libinput = libinput;
        _binder.BindLibinput(libinput);
    }

    internal void OnKey(uint timeMs, uint key, bool pressed)
    {
        _idle.NotifyActivity();
        if (pressed && _seat.Keyboard.State?.IsModActive("Mod1") == true &&
            HandleKeybinding(_seat.Keyboard.KeysymFor(key)))
        {
            return;
        }

        _seat.Keyboard.NotifyKey(timeMs, key, pressed);
    }

    private bool HandleKeybinding(XkbKeysym symbol)
    {
#if DEBUG
        if (symbol == Escape)
        {
            _stop();
            return true;
        }
#endif
        if (_allowVtSwitch && symbol.Value >= SwitchVt1.Value && symbol.Value <= SwitchVt12.Value)
        {
            try
            {
                _host.Session?.SwitchSession((int)(symbol.Value - SwitchVt1.Value + 1));
            }
            catch (NotSupportedException)
            {
                _log.LogDebug("no seat manager to switch sessions");
            }

            return true;
        }

        return false;
    }

    private void ProcessCursorMotion(uint timeMs, double dx = 0, double dy = 0, double? unaccelDx = null, double? unaccelDy = null)
    {
        _cursor.MoveTo(_pointer.X, _pointer.Y);
        var hit = _scene.SurfaceAt(_pointer.X, _pointer.Y);
        if (hit?.Surface is { } surface)
        {
            _seat.Pointer.NotifyMotionAt(timeMs, surface, hit.Value.X, hit.Value.Y, _pointer.X, _pointer.Y);
            _cursor.SetHover(surface, overClient: true);
        }
        else
        {
            _seat.Pointer.NotifyClearFocus();
            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed("left_ptr");
        }

        if (dx != 0 || dy != 0)
        {
            _relativePointer.NotifyMotion(
                (ulong)timeMs * 1000, dx, dy, unaccelDx ?? dx, unaccelDy ?? dy);
        }

        PositionDragIcon();
        _idle.NotifyActivity();
    }

    internal void OnButton(uint timeMs, uint button, bool pressed)
    {
        _seat.Pointer.NotifyButton(timeMs, button, pressed);
        _seat.Pointer.NotifyFrame();
        PressCursorButton(pressed, _pointer.X, _pointer.Y);
        _idle.NotifyActivity();
    }

    private void OnAxis(uint timeMs, PointerAxis axis)
    {
        _seat.Pointer.NotifyAxis(timeMs, axis);
        _seat.Pointer.NotifyFrame();
        _idle.NotifyActivity();
    }

    private void PressCursorButton(bool pressed, double x, double y)
    {
        if (!pressed)
        {
            return;
        }

        var hit = _scene.SurfaceAt(x, y);
        var view = _views.OwnerOf(hit?.Surface);
        var current = _views.FocusedView;
        if (view is null || ReferenceEquals(view, current))
        {
            return;
        }

        if (current is null || !current.IsTransientFor(view))
        {
            _views.Focus(view);
        }
    }

    internal void OnTouchDown(uint timeMs, int id, double x, double y)
    {
        var hit = _scene.SurfaceAt(x, y);
        uint serial = 0;
        if (hit is { Surface: { } surface } point)
        {
            _touchPoints.Down(id, x, y, point.Node);
            serial = _seat.Touch.NotifyDown(surface, timeMs, id, point.X, point.Y);
            _livePoints++;
        }

        if (serial != 0 && _livePoints == 1)
        {
            _touchId = id;
            _touchX = x;
            _touchY = y;
            PressCursorButton(true, x, y);
        }

        _idle.NotifyActivity();
    }

    internal void OnTouchMotion(uint timeMs, int id, double x, double y)
    {
        if (_touchPoints.TryMotion(id, x, y, out var localX, out var localY))
        {
            _seat.Touch.NotifyMotion(timeMs, id, localX, localY);
        }

        if (id == _touchId)
        {
            _touchX = x;
            _touchY = y;
            PositionDragIcon();
        }

        _idle.NotifyActivity();
    }

    internal void OnTouchUp(uint timeMs, int id)
    {
        if (_touchPoints.Up(id))
        {
            _livePoints--;
            _seat.Touch.NotifyUp(timeMs, id);
            if (id == _touchId)
            {
                _touchId = -1;
            }
        }

        _idle.NotifyActivity();
    }

    internal void OnTouchFrame()
    {
        _seat.Touch.NotifyFrame();
        _idle.NotifyActivity();
    }

    private void OnTouchCancel()
    {
        _touchPoints.Clear();
        _livePoints = 0;
        _touchId = -1;
        _seat.Touch.NotifyCancel();
        _idle.NotifyActivity();
    }

    private void PositionDragIcon()
    {
        if (_dragIcon is not { IsDestroyed: false } icon)
        {
            return;
        }

        if (_livePoints > 0 && _touchId >= 0)
        {
            icon.Tree.SetPosition((int)_touchX, (int)_touchY);
        }
        else
        {
            icon.Tree.SetPosition((int)_pointer.X, (int)_pointer.Y);
        }
    }

    public void Dispose()
    {
        _cursor.Dispose();
        _libinput?.Dispose();
    }
}
