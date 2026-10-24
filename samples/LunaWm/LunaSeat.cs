using Basin;
using Basin.Backend.Libinput;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Config;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Scene;
using Basin.Seat;
using Basin.SoftKeyboard;
using Basin.Seat.Backends;

namespace LunaWm;

internal sealed class LunaSeat :
    IDisposable,
    ITouchChrome,
    ITouchActivitySink,
    ILunaEdgeHandler,
    IPinchHandler
{
    internal const int PointerTouchId = -2;

    private const uint SuperL = 0xffeb;
    private const uint SuperR = 0xffec;

    private readonly BasinHost _host;
    private readonly LunaShell _shell;
    private readonly Basin.Seat.Seat _seat;
    private readonly Scene _scene;
    private readonly OutputLayout _layout;
    private readonly LayoutPointer _pointer;
    private readonly CursorController _cursor;
    private readonly SeatIdleSource? _idle;
    private readonly PointerDelivery _delivery;
    private readonly UISurfaceRouter _router;
    private readonly ButtonLatch _latch;
    private readonly ChromeKeyDelivery _keys;
    private readonly LunaEdgeGesture _edge = new();
    private readonly PinchGesture _pinch = new();
    private readonly SeatTouchDriver _touch;
    private readonly SeatBinder _binder;
    private readonly SeatInjector _injector;
    private readonly PointerRefresh _pointerRefresh;
    private readonly PointerGesturesManager? _gestures;
    private readonly BasinLogger _log;
    private LibinputBackend? _libinput;
    private Route _route;
    private IUISurface? _routeSurface;
    private bool _pointerEdge;
    private bool _touchDriving;
    private bool _superAlone;
    private bool _padSwipeOwned;
    private bool _padPinchOwned;
    private readonly Dictionary<int, TouchOwner> _touchOwners = [];
    private bool _disposed;

    public LunaSeat(
        BasinHost host,
        BasinServices services,
        LunaShell shell,
        Scene scene,
        OutputLayout layout,
        CursorImageTheme cursorTheme,
        HookInputSink inputSink,
        UISurfaceIndex index,
        BasinLogger log)
    {
        _host = host;
        _shell = shell;
        _seat = services.Require<Basin.Seat.Seat>();
        _scene = scene;
        _layout = layout;
        _log = log;
        _router = new UISurfaceRouter(scene, index) { ImplicitGrab = true };
        _latch = new ButtonLatch(_seat);
        _keys = new ChromeKeyDelivery(_seat, _router);
        _pointer = new LayoutPointer(layout);
        _cursor = new CursorController(layout)
        {
            Shapes = services.Require<CursorShapeManager>(),
            Capture = services.Find<IScreenCapture>(),
        };
        _cursor.Shapes.CursorRequested += _cursor.ShowImage;
        _cursor.ColorProfiles = services.Find<IColorProfileService>();
        _delivery = new PointerDelivery(_seat, _cursor);
        _idle = services.Find<IIdleSource>() as SeatIdleSource;
        _gestures = services.Find<PointerGesturesManager>();
        _binder = new SeatBinder(_seat, layout, _pointer, _cursor)
        {
            Drm = host.Drm,
            Theme = cursorTheme,
            RelativePointer = services.Find<RelativePointerManager>(),
            Constraints = services.Find<PointerConstraintDriver>(),
        };
        _binder.DeviceAdded += device => BasinReport.Line($"INPUT + {device.Name} keyboard={device.HasKeyboard} pointer={device.HasPointer} touch={device.HasTouch}");
        _binder.DeviceRemoved += device => BasinReport.Line($"INPUT - {device.Name}");
        _binder.Key += OnKey;
        _binder.ModifiersChanged += OnModifiers;
        _binder.Motion += (timeMs, _, _, _, _) => OnMotion(timeMs, fromMotion: true);
        _binder.Button += OnButton;
        _binder.Axis += OnAxis;
        _binder.PointerLeft += () =>
        {
            _router.PointerLeave();
            _seat.Pointer.NotifyClearFocus();
        };
        _pointerRefresh = new PointerRefresh(_scene, host.Loop, () => OnMotion((uint)Environment.TickCount, fromMotion: false));
        _touch = new SeatTouchDriver(_binder, _seat);
        _edge.Handler = this;
        _pinch.Handler = this;
        _touch.Router.Gestures = new TouchGestureSet(_edge, _pinch);
        _touch.Router.HitTester = new SceneTouchHitTester(_scene);
        _touchChrome.Then = this;
        _touch.Router.Chrome = _touchChrome;
        _touch.Router.Activity = this;
        _touch.Routed += (_, _, surface) =>
        {
            if (surface is not null)
            {
                _shell.FocusSurface(surface);
                _shell.NoteClientTouch(surface);
            }
        };

        _seat.Capabilities = SeatCapability.None;
        _seat.Keyboard.SetKeymap(SystemKeymap.Read());
        _seat.Keyboard.SetRepeatInfo(25, 600);
        _seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;

        _injector = new SeatInjector(_binder, _seat, _layout, _pointer)
        {
            Moved = timeMs => OnMotion(timeMs, fromMotion: true),
            MovedBy = (timeMs, _, _) => OnMotion(timeMs, fromMotion: true),
            DeliverButton = OnButton,
            DeliverKey = OnKey,
            DeliverAxis = (timeMs, axis, value, source) =>
                OnAxis(timeMs, new PointerAxis((Wayland.WlPointer.Axis)axis, value, Source: (Wayland.WlPointer.AxisSource)source)),
        };
        inputSink.Route(_injector, _seat, (keyboard, timeMs, key, pressed) =>
            OnKey(timeMs, key, pressed, _shell.IsInputMethodKeyboard(keyboard?.Tag)));

        if (_host.Drm is not null && _host.Session is { } session)
        {
            _libinput = new LibinputBackend(_host.Loop, session);
            _binder.BindLibinput(_libinput);
            _libinput.Gesture += (_, type, gesture) => OnLibinputGesture(type, gesture);
        }
        else if (_host.Parent is { } parent)
        {
            _binder.BindParent(parent);
            parent.PointerAdded += BindParentGestures;
        }
    }

    private enum Route
    {
        Seat,

        Shell,

        Chrome,

        Keyboard,
    }

    private enum TouchOwner
    {
        Chrome,

        Shell,

        Keyboard,
    }

    public UISurfaceRouter Router => _router;

    public SeatInjector Injector => _injector;

    public SeatBinder Binder => _binder;

    public CursorController Cursor => _cursor;

    public double PointerX => _pointer.X;

    public double PointerY => _pointer.Y;

    public bool TouchDriving => _touchDriving;

    public int LiveContacts => _touch.Router.LiveContacts;

    public Modifiers Held => HeldModifiers.From(_seat.Keyboard.State);

    public void AttachConfigurator(InputConfigurator configurator) =>
        configurator.Attach(_seat, _binder, _libinput?.Configuration);

    public void OutputAdded(OutputView view) => _cursor.AddOutput(view.Output, view.Scene!);

    public void OutputRemoved(OutputView view) => _cursor.RemoveOutput(view.Output);

    public void CenterPointer() => _injector.Center();

    public void Warp(double x, double y) => _injector.Warp(x, y);

    public void Refresh() => OnMotion((uint)Environment.TickCount, fromMotion: false);

    public void ClearChromeFocus()
    {
        _router.SetKeyboardFocus(null);
        _router.PointerLeave();
    }

    public void TapAt(double x, double y)
    {
        var time = (uint)Environment.TickCount;
        _seat.SetCapability(SeatCapability.Touch, true);
        _touch.Router.Down(time, 0, x, y);
        _touch.Router.Frame();
        _touch.Router.Up(time + 1, 0);
        _touch.Router.Frame();
    }

    public void TouchDown(int id, double x, double y, uint time)
    {
        _seat.SetCapability(SeatCapability.Touch, true);
        _touch.Router.Down(time, id, x, y);
        _touch.Router.Frame();
    }

    public void TouchMotion(int id, double x, double y, uint time)
    {
        _touch.Router.Motion(time, id, x, y);
        _touch.Router.Frame();
    }

    public void TouchUp(int id, uint time)
    {
        _touch.Router.Up(time, id);
        _touch.Router.Frame();
    }

    public void DragTouch(double x, double y, double dx, double dy, int milliseconds)
    {
        var time = (uint)Environment.TickCount;
        var steps = Math.Max(2, milliseconds / 16);
        TouchDown(0, x, y, time);
        for (var step = 1; step <= steps; step++)
        {
            time += 16;
            TouchMotion(0, x + (dx * step / steps), y + (dy * step / steps), time);
        }

        TouchUp(0, time + 16);
    }

    private readonly Basin.Seat.Backends.TouchChromeChain _touchChrome = new();
    private Basin.Inspector.InspectorInput? _inspector;

    public Basin.Inspector.InspectorInput? Inspector
    {
        get => _inspector;
        set
        {
            _inspector = value;
            _touchChrome.First = value;
        }
    }

    public string CursorState =>
        $"at={_pointer.X:F0},{_pointer.Y:F0} {(_cursor.IsHidden ? "hidden" : "shown")} driving={(_touchDriving ? "touch" : "pointer")} showing={_cursor.Showing}";

    public void SendKey(uint keysym)
    {
        if (!_seat.Keyboard.TryKeycodeForKeysym(keysym, out var keycode, out _))
        {
            _log.Debug($"keyboard: no key in the keymap makes keysym 0x{keysym:x}");
            return;
        }

        var time = (uint)Environment.TickCount;
        var key = keycode - 8;
        _seat.Keyboard.NotifyKey(time, key, true);
        _seat.Keyboard.NotifyKey(time, key, false);
    }

    private void UseTouch()
    {
        if (_touchDriving)
        {
            return;
        }

        _touchDriving = true;
        _cursor.Hide();
        if (_routeSurface is null)
        {
            _router.PointerLeave();
        }
    }

    private void UsePointer()
    {
        _shell.NoteInput(LastInput.Pointer);
        if (!_touchDriving)
        {
            return;
        }

        _touchDriving = false;
        _cursor.Reveal();
    }

    private void OnModifiers()
    {
        _idle?.NotifyActivity();
        var (depressed, latched, locked, group) = _seat.Keyboard.ModifierState;
        _router.Modifiers(depressed, latched, locked, group);
    }

    internal void OnKey(uint timeMs, uint key, bool pressed) => OnKey(timeMs, key, pressed, fromInputMethod: false);

    internal void OnKey(uint timeMs, uint key, bool pressed, bool fromInputMethod)
    {
        _idle?.NotifyActivity();
        if (!fromInputMethod)
        {
            _shell.NoteHardwareKey(pressed);
        }

        var symbol = _seat.Keyboard.KeysymFor(key).Value;
        var isSuper = symbol is SuperL or SuperR;
        if (pressed)
        {
            _superAlone = isSuper;
        }
        else if (!isSuper)
        {
            _superAlone = false;
        }

        var held = Held;
        if (_shell.HandleKey(timeMs, key, symbol, held, pressed, isSuper && !pressed && _superAlone))
        {
            _seat.Keyboard.NotifyKeyConsumed(key, pressed);
            if (!pressed && isSuper)
            {
                _superAlone = false;
            }

            return;
        }

        if (!pressed && isSuper)
        {
            _superAlone = false;
        }

        if (Inspector?.Key(timeMs, key, pressed) == true)
        {
            return;
        }

        if (!_shell.IsLocked || _shell.LockAcceptsChromeKeys)
        {
            if (_keys.Chrome(timeMs, key, pressed))
            {
                return;
            }
        }

        _seat.Keyboard.NotifyKey(timeMs, key, pressed);
    }

    private void OnMotion(uint timeMs, bool fromMotion)
    {
        if (_disposed)
        {
            return;
        }

        if (fromMotion)
        {
            _idle?.NotifyActivity();
        }

        var x = _pointer.X;
        var y = _pointer.Y;
        if (_pointerEdge)
        {
            switch (_touch.Router.SyntheticMotion(PointerTouchId, timeMs, x, y))
            {
                case TouchGestureVerdict.Withhold:
                case TouchGestureVerdict.Claim:
                case TouchGestureVerdict.Owned:
                    _cursor.MoveTo(x, y);
                    return;
                case TouchGestureVerdict.Decline:
                    _pointerEdge = false;
                    ChooseAndPress(timeMs, InputCodes.BtnLeft);
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

        _cursor.MoveTo(x, y);
        if (Inspector is { } inspector && _latch.Held == 0 && inspector.Motion(timeMs, x, y, out var inspectorCursor))
        {
            _router.PointerLeave();
            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed(inspectorCursor ?? "default");
            return;
        }

        if (_route == Route.Keyboard && _latch.Held > 0)
        {
            _shell.KeyboardTouchMotion(PointerTouchId, x, y, timeMs);
            return;
        }

        if (_route == Route.Shell && _latch.Held > 0)
        {
            _shell.ShellMove(PointerTouchId, x, y, timeMs);
            return;
        }

        if (_touchDriving && !fromMotion)
        {
            return;
        }

        var route = _router.PointerMotion(timeMs, x, y);
        if (route.Surface is not null)
        {
            if (route.Entered)
            {
                _seat.Pointer.NotifyClearFocus();
            }

            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed(route.Cursor ?? "default");
            return;
        }

        if (_shell.ShellCursorAt(x, y) is { } shellCursor)
        {
            _seat.Pointer.NotifyClearFocus();
            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed(shellCursor);
            return;
        }

        var hit = _scene.SurfaceAt(x, y);
        _delivery.Motion(timeMs, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, x, y);
    }

    internal void OnButton(uint timeMs, uint button, bool pressed)
    {
        _idle?.NotifyActivity();
        if (Inspector is { } inspector && _latch.Held == 0 && inspector.Button(timeMs, button, pressed))
        {
            return;
        }

        if (button == InputCodes.BtnLeft && pressed && !_pointerEdge && _latch.Held == 0 && !_shell.IsLocked)
        {
            if (_touch.Router.SyntheticDown(PointerTouchId, timeMs, _pointer.X, _pointer.Y) == TouchGestureVerdict.Withhold)
            {
                _pointerEdge = true;
                UsePointer();
                return;
            }

            _touch.Router.SyntheticUp(PointerTouchId, timeMs);
        }

        if (button == InputCodes.BtnLeft && !pressed && _pointerEdge)
        {
            _pointerEdge = false;
            switch (_touch.Router.SyntheticUp(PointerTouchId, timeMs))
            {
                case TouchGestureVerdict.Finish:
                case TouchGestureVerdict.Owned:
                    return;
                case TouchGestureVerdict.Decline:
                    ChooseAndPress(timeMs, InputCodes.BtnLeft);
                    break;
            }
        }

        UsePointer();
        if (pressed)
        {
            if (_latch.Press())
            {
                _route = Choose(timeMs, button);
            }

            Dispatch(timeMs, button, pressed: true);
            return;
        }

        Dispatch(timeMs, button, pressed: false);
        if (_latch.Release())
        {
            _route = Route.Seat;
            _routeSurface = null;
            Refresh();
        }
    }

    private void ChooseAndPress(uint timeMs, uint button)
    {
        if (_latch.Press())
        {
            _route = Choose(timeMs, button);
        }

        Dispatch(timeMs, button, pressed: true);
    }

    private Route Choose(uint timeMs, uint button)
    {
        var x = _pointer.X;
        var y = _pointer.Y;
        if (_shell.KeyboardTouchDown(PointerTouchId, x, y, timeMs))
        {
            _seat.Pointer.NotifyClearFocus();
            return Route.Keyboard;
        }

        _router.PointerMotion(timeMs, x, y);
        if (_router.Hovered is { } surface)
        {
            _routeSurface = surface;
            _seat.Pointer.NotifyClearFocus();
            return Route.Chrome;
        }

        if (_shell.ShellPress(PointerTouchId, x, y, timeMs, button))
        {
            _seat.Pointer.NotifyClearFocus();
            return Route.Shell;
        }

        if (_scene.SurfaceAt(x, y) is { Surface: { } target })
        {
            _shell.FocusSurface(target);
        }

        return Route.Seat;
    }

    private void Dispatch(uint timeMs, uint button, bool pressed)
    {
        switch (_route)
        {
            case Route.Shell:
                if (!pressed && _latch.Held <= 1)
                {
                    _shell.ShellRelease(PointerTouchId, timeMs);
                }

                return;
            case Route.Keyboard:
                if (!pressed && _latch.Held <= 1)
                {
                    _shell.KeyboardTouchUp(PointerTouchId, timeMs);
                }

                return;
            case Route.Chrome:
                if (_routeSurface is not null)
                {
                    _router.PointerButton(timeMs, button, pressed);
                }

                return;
        }

        _latch.SeatButton(timeMs, button, pressed);
    }

    private void OnAxis(uint timeMs, PointerAxis axis)
    {
        _idle?.NotifyActivity();
        if (Inspector?.Axis(timeMs, axis) == true)
        {
            return;
        }

        if (_router.PointerAxis(timeMs, axis))
        {
            return;
        }

        if (_shell.ShellAxis(_pointer.X, _pointer.Y, axis))
        {
            return;
        }

        _seat.Pointer.NotifyAxis(timeMs, axis);
        _seat.Pointer.NotifyFrame();
    }

    private void BindParentGestures(Basin.Backend.Wayland.WaylandPointerDevice pointer)
    {
        pointer.SwipeBegin += (time, fingers) => PadSwipeBegin(time, fingers);
        pointer.SwipeUpdate += (time, dx, dy) => PadSwipeUpdate(time, dx, dy);
        pointer.SwipeEnd += (time, cancelled) => PadSwipeEnd(time, cancelled);
        pointer.PinchBegin += (time, fingers) => PadPinchBegin(time, fingers);
        pointer.PinchUpdate += (time, dx, dy, scale, rotation) => PadPinchUpdate(time, dx, dy, scale, rotation);
        pointer.PinchEnd += (time, cancelled) => PadPinchEnd(time, cancelled);
        pointer.HoldBegin += (time, fingers) => _gestures?.NotifyHoldBegin(time, fingers);
        pointer.HoldEnd += (time, cancelled) => _gestures?.NotifyHoldEnd(time, cancelled);
    }

    private void OnLibinputGesture(Libinput.LibinputEventType type, Libinput.LibinputGestureEvent gesture)
    {
        _idle?.NotifyActivity();
        var time = (uint)(gesture.TimestampMicroseconds / 1000);
        switch (type)
        {
            case Libinput.LibinputEventType.GestureSwipeBegin:
                PadSwipeBegin(time, (uint)gesture.FingerCount);
                break;
            case Libinput.LibinputEventType.GestureSwipeUpdate:
                PadSwipeUpdate(time, gesture.Dx, gesture.Dy);
                break;
            case Libinput.LibinputEventType.GestureSwipeEnd:
                PadSwipeEnd(time, gesture.Cancelled);
                break;
            case Libinput.LibinputEventType.GesturePinchBegin:
                PadPinchBegin(time, (uint)gesture.FingerCount);
                break;
            case Libinput.LibinputEventType.GesturePinchUpdate:
                PadPinchUpdate(time, gesture.Dx, gesture.Dy, gesture.Scale, gesture.AngleDelta);
                break;
            case Libinput.LibinputEventType.GesturePinchEnd:
                PadPinchEnd(time, gesture.Cancelled);
                break;
            case Libinput.LibinputEventType.GestureHoldBegin:
                _gestures?.NotifyHoldBegin(time, (uint)gesture.FingerCount);
                break;
            case Libinput.LibinputEventType.GestureHoldEnd:
                _gestures?.NotifyHoldEnd(time, gesture.Cancelled);
                break;
        }
    }

    private void PadSwipeBegin(uint time, uint fingers)
    {
        _padSwipeOwned = _shell.PadSwipeBegin(fingers, _pointer.X, _pointer.Y, time);
        if (!_padSwipeOwned)
        {
            _gestures?.NotifySwipeBegin(time, fingers);
        }
    }

    private void PadSwipeUpdate(uint time, double dx, double dy)
    {
        if (_padSwipeOwned)
        {
            _shell.PadSwipeUpdate(dx, dy, time);
            return;
        }

        _gestures?.NotifySwipeUpdate(time, dx, dy);
    }

    private void PadSwipeEnd(uint time, bool cancelled)
    {
        if (_padSwipeOwned)
        {
            _padSwipeOwned = false;
            _shell.PadSwipeEnd(cancelled, time);
            return;
        }

        _gestures?.NotifySwipeEnd(time, cancelled);
    }

    private void PadPinchBegin(uint time, uint fingers)
    {
        _padPinchOwned = _shell.PadPinchBegin(fingers, _pointer.X, _pointer.Y, time);
        if (!_padPinchOwned)
        {
            _gestures?.NotifyPinchBegin(time, fingers);
        }
    }

    private void PadPinchUpdate(uint time, double dx, double dy, double scale, double rotation)
    {
        if (_padPinchOwned)
        {
            _shell.PadPinchUpdate(scale, time);
            return;
        }

        _gestures?.NotifyPinchUpdate(time, dx, dy, scale, rotation);
    }

    private void PadPinchEnd(uint time, bool cancelled)
    {
        if (_padPinchOwned)
        {
            _padPinchOwned = false;
            _shell.PadPinchEnd(cancelled, time);
            return;
        }

        _gestures?.NotifyPinchEnd(time, cancelled);
    }

    void ITouchActivitySink.OnTouchActivity()
    {
        _idle?.NotifyActivity();
        _shell.NoteInput(LastInput.Touch);
        UseTouch();
    }

    bool ITouchChrome.TryPress(int id, uint timeMs, double x, double y)
    {
        if (_shell.KeyboardTouchDown(id, x, y, timeMs))
        {
            _touchOwners[id] = TouchOwner.Keyboard;
            return true;
        }

        if (_router.TouchDown(timeMs, id, x, y))
        {
            _touchOwners[id] = TouchOwner.Chrome;
            return true;
        }

        if (_shell.ShellPress(id, x, y, timeMs, InputCodes.BtnLeft))
        {
            _touchOwners[id] = TouchOwner.Shell;
            return true;
        }

        return false;
    }

    void ITouchChrome.Motion(int id, uint timeMs, double x, double y)
    {
        if (!_touchOwners.TryGetValue(id, out var owner))
        {
            return;
        }

        if (owner == TouchOwner.Chrome)
        {
            _router.TouchMotion(timeMs, id, x, y);
            return;
        }

        if (owner == TouchOwner.Keyboard)
        {
            _shell.KeyboardTouchMotion(id, x, y, timeMs);
            return;
        }

        _shell.ShellMove(id, x, y, timeMs);
    }

    void ITouchChrome.Release(int id, uint timeMs, double x, double y)
    {
        if (!_touchOwners.Remove(id, out var owner))
        {
            return;
        }

        if (owner == TouchOwner.Chrome)
        {
            _router.TouchUp(timeMs, id);
            return;
        }

        if (owner == TouchOwner.Keyboard)
        {
            _shell.KeyboardTouchUp(id, timeMs);
            return;
        }

        _shell.ShellRelease(id, timeMs);
    }

    void ITouchChrome.Cancel()
    {
        _router.TouchCancel();
        _shell.CancelKeyboardTouches();
        foreach (var (id, owner) in _touchOwners)
        {
            if (owner == TouchOwner.Shell)
            {
                _shell.ShellCancel(id);
            }
        }

        _touchOwners.Clear();
    }

    bool ILunaEdgeHandler.TryBegin(double x, double y, out LunaEdge edge, out double scale) =>
        _shell.EdgeAt(x, y, out edge, out scale);

    bool ILunaEdgeHandler.Claim(LunaEdge edge, EdgeAxis axis, double x, double y, uint timeMs) =>
        _shell.EdgeClaim(edge, axis, x, y, timeMs);

    void ILunaEdgeHandler.Move(double x, double y, uint timeMs) => _shell.EdgeMove(x, y, timeMs);

    void ILunaEdgeHandler.End(double velocityX, double velocityY, uint timeMs, bool cancelled) =>
        _shell.EdgeEnd(velocityX, velocityY, timeMs, cancelled);

    bool IPinchHandler.Accepts(double x, double y) => _shell.PinchAccepts(x, y);

    bool IPinchHandler.Begin(double centroidX, double centroidY, double spread, uint timeMs) =>
        _shell.PinchBegin(centroidX, centroidY, timeMs);

    void IPinchHandler.Update(double scale, double centroidX, double centroidY, uint timeMs) =>
        _shell.PinchUpdate(scale, timeMs);

    void IPinchHandler.End(double scalePerSecond, uint timeMs) => _shell.PinchEnd(scalePerSecond, timeMs, cancelled: false);

    void IPinchHandler.Cancel() => _shell.PinchEnd(0, 0, cancelled: true);

    public void Dispose()
    {
        _disposed = true;
        _pointerRefresh.Dispose();
        _cursor.Dispose();
        _libinput?.Dispose();
    }
}
