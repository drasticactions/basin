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

namespace EightWm;

internal sealed partial class ShellSeat :
    IDisposable,
    ITouchChrome,
    ITouchActivitySink,
    IEdgeSwipeHandler
{
    private static readonly XkbKeysym SwitchVt1 = XkbKeysym.FromName("XF86Switch_VT_1");
    private static readonly XkbKeysym SwitchVt12 = XkbKeysym.FromName("XF86Switch_VT_12");

    private readonly Basin.Host.BasinHost _host;
    private readonly Shell _shell;
    private readonly OutputDriver _outputs;
    private readonly Basin.Seat.Seat _seat;
    private readonly Scene _scene;
    private readonly OutputLayout _layout;
    private readonly LayoutPointer _pointer;
    private readonly CursorController _cursor;
    private readonly CursorImageTheme _cursorTheme;
    private readonly SeatIdleSource _idle;
    private readonly RelativePointerManager _relativePointer;
    private readonly EdgeSwipeGesture _edgeGesture = new();
    private readonly SeatTouchDriver _touch;
    private ShellView? _edgeView;
    private bool _pointerEdge;
    private readonly ILogger _log;

    private readonly SeatBinder _binder;
    private readonly SeatInjector _injector;

    private LibinputBackend? _libinput;

    public ShellSeat(
        Basin.Host.BasinHost host,
        BasinServices services,
        Shell shell,
        OutputDriver outputs,
        Scene scene,
        OutputLayout layout,
        CursorImageTheme cursorTheme,
        Basin.Seat.Backends.HookInputSink inputSink,
        ILogger log)
    {
        _host = host;
        _shell = shell;
        _outputs = outputs;
        _seat = services.Require<Basin.Seat.Seat>();
        _scene = scene;
        _layout = layout;
        _cursorTheme = cursorTheme;
        _log = log;

        _pointer = new LayoutPointer(layout);
        _cursor = new CursorController(layout)
        {
            Shapes = services.Require<CursorShapeManager>(),
            Capture = services.Find<Basin.Capabilities.IScreenCapture>(),
        };
        _cursor.Shapes.CursorRequested += _cursor.ShowImage;
        _cursor.ColorProfiles = services.Find<Basin.Capabilities.IColorProfileService>();
        _relativePointer = services.Require<RelativePointerManager>();
        _idle = (services.Find<IIdleSource>() as SeatIdleSource)!;
        _binder = new SeatBinder(_seat, layout, _pointer, _cursor)
        {
            Drm = host.Drm,
            Theme = cursorTheme,
        };
        _binder.DeviceAdded += device => Console.WriteLine(
            $"INPUT + {device.Name} keyboard={device.HasKeyboard} pointer={device.HasPointer} " +
            $"touch={device.HasTouch}");
        _binder.DeviceRemoved += device => Console.WriteLine($"INPUT - {device.Name}");
        _binder.Key += OnKey;
        _binder.ModifiersChanged += _idle.NotifyActivity;
        _binder.Motion += (timeMs, dx, dy, unaccelDx, unaccelDy) =>
            ProcessCursorMotion(timeMs, dx, dy, unaccelDx, unaccelDy);
        _binder.Button += OnButton;
        _binder.Axis += OnAxis;
        _binder.PointerLeft += _seat.Pointer.NotifyClearFocus;
        _touch = new SeatTouchDriver(_binder, _seat);
        _edgeGesture.Handler = this;
        _touch.Router.Gestures = _edgeGesture;
        _touch.Router.HitTester = new SceneTouchHitTester(_scene);
        _touch.Router.Chrome = this;
        _touch.Router.Activity = this;
        _touch.Routed += (_, _, surface) =>
        {
            if (surface is not null)
            {
                _shell.Focus(_shell.OwnerOf(surface));
            }
        };

        _seat.Capabilities = SeatCapability.None;
        _seat.Keyboard.SetKeymap(SystemKeymap.Read());
        _seat.Keyboard.SetRepeatInfo(25, 600);
        _seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;

        _outputs.Added += view => _cursor.AddOutput(view.Output, view.Scene!);
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
        _injector = new SeatInjector(_binder, _seat, _layout, _pointer)
        {
            Moved = timeMs => ProcessCursorMotion(timeMs),
            MovedBy = (timeMs, dx, dy) => ProcessCursorMotion(timeMs, dx, dy),
            DeliverButton = OnButton,
            DeliverKey = OnKey,
        };
        inputSink.OnPointerMotionAbsolute = _injector.MotionAbsolute;
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

    internal double PointerX => _pointer.X;

    internal double PointerY => _pointer.Y;

    internal void Refocus() => ProcessCursorMotion((uint)Environment.TickCount, fromMotion: false);

    internal void CenterCursor() => _injector.Center();

    private bool _touchDriving;

    internal bool TouchDriving => _touchDriving;

    private void UseTouch()
    {
        if (_touchDriving)
        {
            return;
        }

        _touchDriving = true;
        _cursor.Hide();
    }

    private void UsePointer()
    {
        if (!_touchDriving)
        {
            return;
        }

        _touchDriving = false;
        _cursor.Reveal();
    }

    internal void WarpTo(double x, double y) => _injector.Warp(x, y);

    internal void InjectKey(uint code, bool pressed) => _injector.Key(code, pressed);

    internal void ClickAt()
    {
        var time = (uint)Environment.TickCount;
        OnButton(time, InputCodes.BtnLeft, pressed: true);
        OnButton(time, InputCodes.BtnLeft, pressed: false);
    }

    internal void ButtonAt(bool pressed) =>
        OnButton((uint)Environment.TickCount, InputCodes.BtnLeft, pressed);

    internal void TapAt(double x, double y)
    {
        var time = (uint)Environment.TickCount;
        _seat.SetCapability(SeatCapability.Touch, true);
        _touch.Router.Down(time, 0, x, y);
        _touch.Router.Frame();
        _touch.Router.Up(time + 1, 0);
        _touch.Router.Frame();
    }

    internal void DescribeCursor(IOutput output, ImageDescription? description) =>
        _cursor.Describe(output, description);

    internal string CursorState =>
        $"at={_pointer.X:F0},{_pointer.Y:F0} {(_cursor.IsHidden ? "hidden" : "shown")} " +
        $"driving={(_touchDriving ? "touch" : "pointer")} by={_cursor.DrawnBy} showing={_cursor.Showing}";

    private void WireLibinput()
    {
        var libinput = new LibinputBackend(_host.Loop, _host.Session!);
        _libinput = libinput;
        _binder.BindLibinput(libinput);
    }

    internal void OnKey(uint timeMs, uint key, bool pressed)
    {
        _idle.NotifyActivity();
        var symbol = _seat.Keyboard.KeysymFor(key);
        var view = _shell.ViewAt(_pointer.X, _pointer.Y);
        if (pressed)
        {
            if (HandleChord(key))
            {
                _seat.Keyboard.NotifyKeyConsumed(key, pressed);
                return;
            }

            if (_shell.HandleChord(view, symbol, SuperHeld, ShiftHeld, AltHeld, ControlHeld))
            {
                _seat.Keyboard.NotifyKeyConsumed(key, pressed);
                return;
            }
        }
        else if (_shell.HandleSuperRelease(view, symbol))
        {
            _seat.Keyboard.NotifyKeyConsumed(key, pressed);
            return;
        }

        _seat.Keyboard.NotifyKey(timeMs, key, pressed);
    }

    private bool HandleChord(uint key)
    {
        var symbol = _seat.Keyboard.KeysymFor(key);
        if (symbol.Value >= SwitchVt1.Value && symbol.Value <= SwitchVt12.Value)
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

    private void ProcessCursorMotion(
        uint timeMs, double dx = 0, double dy = 0, double? unaccelDx = null, double? unaccelDy = null,
        bool fromMotion = true)
    {
        if (_pointerEdge)
        {
            switch (_touch.Router.SyntheticMotion(PointerTouchId, timeMs, _pointer.X, _pointer.Y))
            {
                case TouchGestureVerdict.Withhold:
                case TouchGestureVerdict.Claim:
                case TouchGestureVerdict.Owned:
                    _cursor.MoveTo(_pointer.X, _pointer.Y);
                    _idle.NotifyActivity();
                    return;
                case TouchGestureVerdict.Decline:
                    _pointerEdge = false;
                    RouteButton(timeMs, InputCodes.BtnLeft, pressed: true);
                    break;
                default:
                    _pointerEdge = false;
                    break;
            }
        }

        if (fromMotion)
        {
            UsePointer();
        }

        _cursor.MoveTo(_pointer.X, _pointer.Y);
        if (_splitView is { } dragging)
        {
            _shell.DragSplitter(dragging, _pointer.X - dragging.Box.X, _pointer.Y - dragging.Box.Y);
            TakeCursor(Shell.SplitterCursor(dragging));
            _idle.NotifyActivity();
            return;
        }

        _shell.TrackStartContact(_pointer.X - _startView, _pointer.Y - _startViewY);
        if (_shell.StartMove(_pointer.X - _startView, _pointer.Y - _startViewY, PointerTouchId))
        {
            _idle.NotifyActivity();
            return;
        }

        var hoverView = _shell.ViewAt(_pointer.X, _pointer.Y);
        if (_shell.ChromeMove(
                hoverView, _pointer.X - hoverView.Box.X, _pointer.Y - hoverView.Box.Y, PointerTouchId))
        {
            TakeCursor(Shell.PointerCursor);
            _idle.NotifyActivity();
            return;
        }

        if (fromMotion)
        {
            _shell.TrackCorner(hoverView, _pointer.X - hoverView.Box.X, _pointer.Y - hoverView.Box.Y);
            _shell.HoverCharms(hoverView, _pointer.X - hoverView.Box.X, _pointer.Y - hoverView.Box.Y);
            _shell.HoverTitle(hoverView, _pointer.X - hoverView.Box.X, _pointer.Y - hoverView.Box.Y);
        }

        var hit = _scene.SurfaceAt(_pointer.X, _pointer.Y);
        if (_shell.ChromeCursorAt(
                hoverView, _pointer.X - hoverView.Box.X, _pointer.Y - hoverView.Box.Y,
                hit?.Surface) is { } chrome)
        {
            TakeCursor(chrome);
            _idle.NotifyActivity();
            return;
        }

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
            _relativePointer.NotifyMotion((ulong)timeMs * 1000, dx, dy, unaccelDx ?? dx, unaccelDy ?? dy);
        }

        _idle.NotifyActivity();
    }

    private int _chromeKey = -1;

    internal void RefreshCursor()
    {
        if ((_seat.Capabilities & SeatCapability.Pointer) == 0)
        {
            return;
        }

        var key = Shell.ChromeKey(_shell.ViewAt(_pointer.X, _pointer.Y));
        if (key == _chromeKey)
        {
            return;
        }

        _chromeKey = key;
        if (_cursor.IsHidden)
        {
            return;
        }

        var view = _shell.ViewAt(_pointer.X, _pointer.Y);
        _shell.RefreshHot(view, _pointer.X - view.Box.X, _pointer.Y - view.Box.Y);
        ProcessCursorMotion((uint)Environment.TickCount, fromMotion: false);
    }

    private void TakeCursor(string name)
    {
        _seat.Pointer.NotifyClearFocus();
        _cursor.SetHover(null, overClient: false);
        _cursor.ShowNamed(name);
    }

    private ShellView? _splitView;
    private double _startView;
    private double _startViewY;

    internal const int PointerTouchId = -2;

    internal void OnButton(uint timeMs, uint button, bool pressed)
    {
        if (button == InputCodes.BtnLeft && pressed && !_pointerEdge &&
            _touch.Router.SyntheticDown(PointerTouchId, timeMs, _pointer.X, _pointer.Y) ==
            TouchGestureVerdict.Withhold)
        {
            _pointerEdge = true;
            return;
        }

        if (button == InputCodes.BtnLeft && !pressed && _pointerEdge)
        {
            _pointerEdge = false;
            switch (_touch.Router.SyntheticUp(PointerTouchId, timeMs))
            {
                case TouchGestureVerdict.Finish:
                    return;
                case TouchGestureVerdict.Decline:
                    RouteButton(timeMs, InputCodes.BtnLeft, pressed: true);
                    break;
            }
        }

        RouteButton(timeMs, button, pressed);
    }

    private void RouteButton(uint timeMs, uint button, bool pressed)
    {
        _idle.NotifyActivity();
        UsePointer();
        if (button == InputCodes.BtnLeft && !pressed && _splitView is { } dragging)
        {
            _shell.EndSplitDrag(dragging);
            _splitView = null;
            return;
        }

        if (button == InputCodes.BtnLeft && !pressed)
        {
            var releaseView = _shell.ViewAt(_pointer.X, _pointer.Y);
            if (_shell.ChromeRelease(
                    releaseView, _pointer.X - releaseView.Box.X, _pointer.Y - releaseView.Box.Y, PointerTouchId))
            {
                return;
            }

            if (_shell.StartRelease(_pointer.X - _startView, _pointer.Y - _startViewY, PointerTouchId))
            {
                return;
            }
        }

        if (button == InputCodes.BtnLeft && pressed)
        {
            var charmsView = _shell.ViewAt(_pointer.X, _pointer.Y);
            if (_shell.CornerClick(
                    charmsView, _pointer.X - charmsView.Box.X, _pointer.Y - charmsView.Box.Y))
            {
                return;
            }

            if (_shell.ChromePress(
                    charmsView, _pointer.X - charmsView.Box.X, _pointer.Y - charmsView.Box.Y, PointerTouchId))
            {
                return;
            }

            var view = charmsView;
            if (_shell.BeginSplitDrag(view, _pointer.X - view.Box.X, _pointer.Y - view.Box.Y))
            {
                _splitView = view;
                _seat.Pointer.NotifyClearFocus();
                return;
            }

            if (_scene.SurfaceAt(_pointer.X, _pointer.Y) is null)
            {
                _startView = view.Box.X;
                _startViewY = view.Box.Y;
                if (_shell.StartPress(view, _pointer.X - view.Box.X, _pointer.Y - view.Box.Y, PointerTouchId))
                {
                    return;
                }
            }
        }

        if (button == InputCodes.BtnRight && pressed && _scene.SurfaceAt(_pointer.X, _pointer.Y) is { Surface: { } target })
        {
            _shell.Focus(_shell.OwnerOf(target));
            _shell.ToggleTitle(_shell.ViewAt(_pointer.X, _pointer.Y));
            return;
        }

        _seat.Pointer.NotifyButton(timeMs, button, pressed);
        _seat.Pointer.NotifyFrame();
        if (pressed && _scene.SurfaceAt(_pointer.X, _pointer.Y) is { Surface: { } surface })
        {
            _shell.Focus(_shell.OwnerOf(surface));
        }
    }

    private void OnAxis(uint timeMs, PointerAxis axis)
    {
        _idle.NotifyActivity();
        if (ControlHeld && axis.Axis == Wayland.WlPointer.Axis.VerticalScroll && axis.Value != 0)
        {
            var view = _shell.ViewAt(_pointer.X, _pointer.Y);
            _shell.ToggleZoom(
                view, zoomOut: axis.Value > 0, _pointer.X - view.Box.X, _pointer.Y - view.Box.Y);
            return;
        }

        _seat.Pointer.NotifyAxis(timeMs, axis);
        _seat.Pointer.NotifyFrame();
    }

    internal bool ControlHeld => _seat.Keyboard.State?.IsModActive("Control") == true;

    internal bool SuperHeld => _seat.Keyboard.State?.IsModActive("Mod4") == true;

    internal bool AltHeld => _seat.Keyboard.State?.IsModActive("Mod1") == true;

    internal bool ShiftHeld => _seat.Keyboard.State?.IsModActive("Shift") == true;

    private int _splitTouch = -1;

    void ITouchActivitySink.OnTouchActivity()
    {
        _idle.NotifyActivity();
        UseTouch();
    }

    bool ITouchChrome.TryPress(int id, uint timeMs, double x, double y)
    {
        var view = _shell.ViewAt(x, y);
        if (_shell.ChromePress(view, x - view.Box.X, y - view.Box.Y, id))
        {
            return true;
        }

        if (_splitTouch < 0 && _shell.BeginSplitDrag(view, x - view.Box.X, y - view.Box.Y))
        {
            _splitView = view;
            _splitTouch = id;
            return true;
        }

        if (_scene.SurfaceAt(x, y) is not null)
        {
            return false;
        }

        _shell.StartPress(view, x - view.Box.X, y - view.Box.Y, id);
        return true;
    }

    void ITouchChrome.Motion(int id, uint timeMs, double x, double y)
    {
        if (id == _splitTouch && _splitView is { } dragging)
        {
            _shell.DragSplitter(dragging, x - dragging.Box.X, y - dragging.Box.Y);
            return;
        }

        var view = _shell.ViewAt(x, y);
        if (_shell.ChromeMove(view, x - view.Box.X, y - view.Box.Y, id))
        {
            return;
        }

        _ = _shell.StartMove(x - view.Box.X, y - view.Box.Y, id);
    }

    void ITouchChrome.Release(int id, uint timeMs, double x, double y)
    {
        if (id == _splitTouch)
        {
            _splitTouch = -1;
            if (_splitView is { } dragging)
            {
                _shell.EndSplitDrag(dragging);
                _splitView = null;
            }

            return;
        }

        var view = _shell.ViewAt(x, y);
        if (_shell.ChromeRelease(view, x - view.Box.X, y - view.Box.Y, id))
        {
            return;
        }

        _ = _shell.StartRelease(x - view.Box.X, y - view.Box.Y, id);
    }

    void ITouchChrome.Cancel()
    {
        _shell.ChromeCancel();
        if (_splitView is { } dragging)
        {
            _shell.EndSplitDrag(dragging);
            _splitView = null;
        }

        _splitTouch = -1;
    }

    bool IEdgeSwipeHandler.TryArea(double layoutX, double layoutY, out EdgeSwipeArea area)
    {
        var view = _shell.ViewAt(layoutX, layoutY);
        _edgeView = view;
        _edgeGesture.Recognizer.BandWidth = _shell.EdgeBandNow;
        area = new EdgeSwipeArea(view.Box.X, view.Box.Y, view.Box.Width, view.Box.Height);
        return true;
    }

    void IEdgeSwipeHandler.Claimed(EdgeSwipeRecognizer recognizer)
    {
        if (_edgeView is { } view)
        {
            _shell.TrackEdgeGesture(view, recognizer);
        }
    }

    void IEdgeSwipeHandler.Track(EdgeSwipeRecognizer recognizer)
    {
        if (_edgeView is { } view)
        {
            _shell.TrackEdgeGesture(view, recognizer);
        }
    }

    void IEdgeSwipeHandler.Finished(EdgeSwipeRecognizer recognizer)
    {
        if (_edgeView is { } view)
        {
            _edgeView = null;
            _shell.FinishEdgeGesture(view, recognizer);
        }
    }

    public void Dispose()
    {
        _cursor.Dispose();
        _libinput?.Dispose();
    }
}
