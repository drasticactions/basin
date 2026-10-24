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
using Xkb;

using Basin.Diagnostics;

namespace Dam;

internal sealed class DamSeat :
    IDisposable,
    ITouchPointerTarget,
    ITouchActivitySink
{
    private static readonly XkbKeysym Escape = XkbKeysym.FromName("Escape");

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
    private readonly PointerDelivery _delivery;
    private readonly Action _stop;
    private readonly BasinLogger _log;

    private readonly SeatBinder _binder;
    private readonly SeatInjector _injector;

    private readonly SeatTouchDriver _touch;
    private Basin.Inspector.InspectorInput? _inspector;

    private LibinputBackend? _libinput;
    private readonly PointerRefresh _pointerRefresh;
    private int _touchId = -1;
    private readonly DragIconFollower _dragIcon;
    private ChordMatcher? _inspectorChord;

    public DamSeat(
        Basin.Host.BasinHost host,
        BasinServices services,
        DamViews views,
        OutputDriver outputs,
        Scene scene,
        OutputLayout layout,
        CursorImageTheme cursorTheme,
        Basin.Seat.Backends.HookInputSink inputSink,
        SeatIdleSource idle,
        bool allowVtSwitch,
        Action stop,
        BasinLogger log)
    {
        _host = host;
        _seat = services.Require<Basin.Seat.Seat>();
        _views = views;
        _outputs = outputs;
        _scene = scene;
        _layout = layout;
        _cursorTheme = cursorTheme;
        _idle = idle;
        _stop = stop;
        _log = log;

        _pointer = new LayoutPointer(layout);
        _cursor = new CursorController(layout)
        {
            Shapes = services.Require<CursorShapeManager>(),
            Capture = services.Find<Basin.Capabilities.IScreenCapture>(),
        };
        _cursor.Shapes.CursorRequested += _cursor.ShowImage;
        _delivery = new PointerDelivery(_seat, _cursor);
        _binder = new SeatBinder(_seat, layout, _pointer, _cursor)
        {
            Drm = host.Drm,
            Theme = cursorTheme,
            RelativePointer = services.Find<RelativePointerManager>(),
            VtSwitching = allowVtSwitch,
            Constraints = services.Find<PointerConstraintDriver>(),
        };
        _binder.Key += OnKey;
        _binder.ModifiersChanged += _idle.NotifyActivity;
        _binder.Motion += ProcessCursorMotion;
        _binder.Button += OnButton;
        _binder.Axis += OnAxis;
        _binder.PointerLeft += _seat.Pointer.NotifyClearFocus;
        _dragIcon = new DragIconFollower(_seat, () => _scene.Root, () => (_pointer.X, _pointer.Y));
        _touch = new SeatTouchDriver(_binder, _seat);
        _dragIcon.Touch = _touch.Router;
        _touch.Router.HitTester = new SceneTouchHitTester(_scene);
        _touch.Router.Activity = this;
        _touch.AttachPointer(this);
        _touch.Routed += (id, kind, surface) =>
        {
            if (kind == TouchTargetKind.Client && _touchId < 0)
            {
                _touchId = id;
                _dragIcon.SetTouchSlot(id);
                if (_touch.Router.TryGetPosition(id, out var x, out var y))
                {
                    PressCursorButton(true, x, y);
                }
            }
        };

        _seat.Capabilities = SeatCapability.None;
        _seat.Keyboard.SetKeymap(SystemKeymap.Read());
        _seat.Keyboard.SetRepeatInfo(25, 600);
        _seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;

        _pointerRefresh = new PointerRefresh(
            _scene, _host.Loop, () => ProcessCursorMotion((uint)Environment.TickCount));

        _outputs.Added += view => _cursor.AddOutput(view.Output, view.Scene);
        _outputs.Removed += view => _cursor.RemoveOutput(view.Output);

        _injector = new SeatInjector(_binder, _seat, _layout, _pointer)
        {
            Moved = timeMs => ProcessCursorMotion(timeMs),
            MovedBy = (timeMs, dx, dy) => ProcessCursorMotion(timeMs, dx, dy),
            DeliverButton = OnButton,
            DeliverKey = OnKey,
            DeliverAxis = (timeMs, axis, value, source) =>
                OnAxis(timeMs, new PointerAxis((Wayland.WlPointer.Axis)axis, value, Source: (Wayland.WlPointer.AxisSource)source)),
        };
        inputSink.Route(_injector, _seat);

        if (_host.Drm is not null)
        {
            WireLibinput();
        }
        else if (_host.Parent is { } parent)
        {
            _binder.BindParent(parent);
        }
    }

    internal TouchRouter TouchRouter => _touch.Router;

    internal Basin.Inspector.InspectorInput? Inspector
    {
        get => _inspector;
        set
        {
            _inspector = value;
            _touch.Router.Chrome = value;
        }
    }

    internal Action? ToggleInspector { get; set; }

    internal (double X, double Y) PointerPosition => (_pointer.X, _pointer.Y);

    internal void Warp(double x, double y) => _injector.Warp(x, y);

    internal SeatInjector Injector => _injector;

    public void CenterCursor() => _injector.Center();

    internal void AttachConfigurator(InputConfigurator configurator)
    {
        configurator.Rejected += outcome =>
            _log.Warn($"input: {InputSettingNames.Key(outcome.Setting)} on {outcome.DeviceName ?? "every device"}: {outcome.Result}");
        configurator.Attach(_seat, _binder, _libinput?.Configuration);
    }

    private void WireLibinput()
    {
        var libinput = new LibinputBackend(_host.Loop, _host.Session!);
        _libinput = libinput;
        _binder.BindLibinput(libinput);
    }

    private readonly HashSet<uint> _accessKeys = [];

    internal Func<uint, bool>? AccessChord { get; set; }

    internal SeatBinder Binder => _binder;

    internal CursorController Cursor => _cursor;

    internal void OnKey(uint timeMs, uint key, bool pressed)
    {
        _idle.NotifyActivity();
        if (ToggleInspector is { } toggle && InspectorChord(key, pressed))
        {
            if (pressed)
            {
                toggle();
            }

            return;
        }

        if (Inspector?.Key(timeMs, key, pressed) == true)
        {
            return;
        }

        if (!pressed && _accessKeys.Remove(key))
        {
            return;
        }

        if (pressed && _seat.Keyboard.State is { } state && state.IsModActive("Mod4") && state.IsModActive("Mod1") &&
            AccessChord?.Invoke(key) == true)
        {
            _accessKeys.Add(key);
            return;
        }

        if (pressed && _seat.Keyboard.State?.IsModActive("Mod1") == true &&
            HandleKeybinding(_seat.Keyboard.KeysymFor(key)))
        {
            return;
        }

        _seat.Keyboard.NotifyKey(timeMs, key, pressed);
    }

    private bool InspectorChord(uint key, bool pressed)
    {
        _inspectorChord ??= NewInspectorChord(_seat.Keyboard);
        if (!_inspectorChord.Matches(key, pressed))
        {
            return false;
        }

        _seat.Keyboard.NotifyKeyConsumed(key, pressed);
        return true;
    }

    private static ChordMatcher NewInspectorChord(Basin.Seat.SeatKeyboard keyboard)
    {
        var chord = new ChordMatcher(keyboard);
        chord.Set(Basin.Config.Keysym.FromName("F12"), Basin.Config.Modifiers.Super);
        return chord;
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
        return false;
    }

    private void ProcessCursorMotion(uint timeMs, double dx = 0, double dy = 0, double? unaccelDx = null, double? unaccelDy = null)
    {
        _cursor.MoveTo(_pointer.X, _pointer.Y);
        if (Inspector?.Motion(timeMs, _pointer.X, _pointer.Y, out var cursor) == true)
        {
            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed(cursor ?? "default");
            PositionDragIcon();
            _idle.NotifyActivity();
            return;
        }

        var hit = _scene.SurfaceAt(_pointer.X, _pointer.Y);
        _delivery.Motion(timeMs, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, _pointer.X, _pointer.Y);

        PositionDragIcon();
        _idle.NotifyActivity();
    }

    internal void OnButton(uint timeMs, uint button, bool pressed)
    {
        if (Inspector?.Button(timeMs, button, pressed) == true)
        {
            _idle.NotifyActivity();
            return;
        }

        _seat.Pointer.NotifyButton(timeMs, button, pressed);
        _seat.Pointer.NotifyFrame();
        PressCursorButton(pressed, _pointer.X, _pointer.Y);
        _idle.NotifyActivity();
    }

    private void OnAxis(uint timeMs, PointerAxis axis)
    {
        if (Inspector?.Axis(timeMs, axis) == true)
        {
            _idle.NotifyActivity();
            return;
        }

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

    void ITouchActivitySink.OnTouchActivity()
    {
        PositionDragIcon();
        _idle.NotifyActivity();
    }

    void ITouchPointerTarget.Warp(uint timeMs, double x, double y)
    {
        _pointer.Warp(x, y);
        ProcessCursorMotion(timeMs);
    }

    void ITouchPointerTarget.Button(uint timeMs, uint button, bool pressed) =>
        OnButton(timeMs, button, pressed);

    private void PositionDragIcon() => _dragIcon.Follow();

    public void Dispose()
    {
        _pointerRefresh.Dispose();
        _cursor.Dispose();
        _libinput?.Dispose();
    }
}
