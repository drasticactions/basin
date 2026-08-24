using Basin;
using Basin.Backend.Libinput;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Basin.Host;
using Basin.Scene;
using Basin.Seat;
using Basin.Seat.Backends;
using Xkb;

namespace PlasmaHost;

internal sealed class PlasmaHostSeat :
    IDisposable, ITouchPointerTarget, ITouchActivitySink, ITouchChrome, ITouchDragHandler
{
    private static readonly XkbKeysym Escape = XkbKeysym.FromName("Escape");
    private const int RingMargin = 8;
    private const int CornerZone = 24;
    private const int TouchRingMargin = 32;
    private const int TouchCornerZone = 40;

    private enum DragMode
    {
        None,
        Move,
        Resize,
    }

    private readonly Basin.Host.BasinHost _host;
    private readonly Basin.Seat.Seat _seat;
    private readonly PlasmaHostWindows _windows;
    private readonly Scene _scene;
    private readonly OutputLayout _layout;
    private readonly LayoutPointer _pointer;
    private readonly CursorController _cursor;
    private readonly SeatIdleSource? _idle;
    private readonly RelativePointerManager? _relativePointer;
    private readonly Action _stop;
    private readonly SeatBinder _binder;
    private readonly SeatInjector _injector;
    private readonly SeatTouchDriver _touch;
    private LibinputBackend? _libinput;
    private Basin.Session.ISession? _session;
    private SceneSurface? _dragIcon;
    private DragMode _mode;
    private PlasmaHostView? _grabView;
    private double _grabX;
    private double _grabY;
    private Basin.Shell.Xdg.ResizeEdges _grabEdges;
    private Box _grabStart;
    private (Basin.Scene.Frame Frame, PlasmaHostView Owner)? _frameHover;
    private (Basin.Scene.Frame Frame, PlasmaHostView Owner)? _framePress;
    private (Basin.Scene.Frame Frame, PlasmaHostView Owner)? _touchFramePress;
    private int? _frameTouchSlot;
    private Basin.Scene.Frame? _openMenu;
    private bool _menuHovering;

    public PlasmaHostSeat(
        Basin.Host.BasinHost host,
        BasinServices services,
        PlasmaHostWindows windows,
        OutputDriver outputs,
        Scene scene,
        OutputLayout layout,
        CursorImageTheme cursorTheme,
        Basin.Seat.Backends.HookInputSink inputSink,
        Action stop)
    {
        _host = host;
        _seat = services.Require<Basin.Seat.Seat>();
        _windows = windows;
        _scene = scene;
        _layout = layout;
        _stop = stop;
        _idle = services.Find<IIdleSource>() as SeatIdleSource;

        _pointer = new LayoutPointer(layout);
        _cursor = new CursorController(layout)
        {
            Shapes = services.Require<CursorShapeManager>(),
            Capture = services.Find<IScreenCapture>(),
        };
        _cursor.Shapes.CursorRequested += _cursor.ShowImage;
        _relativePointer = services.Find<RelativePointerManager>();
        _binder = new SeatBinder(_seat, layout, _pointer, _cursor)
        {
            Drm = host.Drm,
            Theme = cursorTheme,
        };
        _binder.Key += OnKey;
        _binder.Motion += ProcessCursorMotion;
        _binder.Button += OnButton;
        _binder.Axis += OnAxis;
        _binder.PointerLeft += _seat.Pointer.NotifyClearFocus;
        _touch = new SeatTouchDriver(_binder, _seat);
        _touch.Router.HitTester = new SceneTouchHitTester(_scene);
        _touch.Router.Activity = this;
        _touch.Router.Chrome = this;
        _touch.MoveResize.Handler = this;
        _touch.AttachPointer(this);
        _touch.Router.Gestures = services.Find<Basin.Plasma.PlasmaScreenEdges>()?.TouchGesture;
        _touch.Routed += (id, kind, surface) =>
        {
            if (kind == TouchTargetKind.Client)
            {
                _windows.FocusAt(surface);
            }
        };

        _seat.Capabilities = SeatCapability.None;
        _seat.Keyboard.SetKeymap(SystemKeymap.Read());
        _seat.Keyboard.SetRepeatInfo(25, 600);
        _seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;

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

        outputs.Added += view => _cursor.AddOutput(view.Output, view.Scene);
        outputs.Removed += view => _cursor.RemoveOutput(view.Output);

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

        _windows.MoveGrabRequested += BeginMove;
        _windows.ResizeGrabRequested += BeginResize;

        if (_host.Drm is not null)
        {
            _libinput = new LibinputBackend(_host.Loop, _host.Session!);
            _binder.BindLibinput(_libinput);
        }
        else if (_host.Parent is { } parent)
        {
            _binder.BindParent(parent);
        }
        else
        {
            BindHeadlessInput();
        }
    }

    private void BindHeadlessInput()
    {
        try
        {
            var session = Basin.Session.SeatdSession.Open(_host.Loop);
            _session = session;
            _libinput = new LibinputBackend(_host.Loop, session);
            _binder.BindLibinput(_libinput);
        }
        catch (Exception error) when (
            error is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or IOException)
        {
            Basin.Diagnostics.BasinLog.Info(
                $"headless run without a seat ({error.Message}); input stays on injected commands");
        }

        _binder.EnsurePointerCapability();
        _binder.EnsureCursorLoaded();
        _cursor.MoveTo(_pointer.X, _pointer.Y);
    }

    public CursorImage? CursorSprite
    {
        get
        {
            if (_cursor.Images is not { } images)
            {
                return null;
            }

            var showing = _cursor.Showing;
            return showing is "nothing" or "client" ? null : images.Named(showing);
        }
    }

    public double PointerX => _pointer.X;

    public double PointerY => _pointer.Y;

    public void Warp(double x, double y) => _injector.Warp(x, y);

    public void InjectButton(uint button, bool pressed) => _injector.Button(button, pressed);

    public void InjectKey(uint key, bool pressed) => _injector.Key(key, pressed);

    public void CenterCursor() => _injector.Center();

    private void OnKey(uint timeMs, uint key, bool pressed)
    {
        _idle?.NotifyActivity();
        if (pressed && _seat.Keyboard.State?.IsModActive("Mod1") == true &&
            _seat.Keyboard.KeysymFor(key) == Escape)
        {
            _stop();
            return;
        }

        _seat.Keyboard.NotifyKey(timeMs, key, pressed);
    }

    private void ProcessCursorMotion(uint timeMs, double dx = 0, double dy = 0, double? unaccelDx = null, double? unaccelDy = null)
    {
        _cursor.MoveTo(_pointer.X, _pointer.Y);
        if (_touch.MoveResize is not { Dragging: true } && DragTo(_pointer.X, _pointer.Y))
        {
            PositionDragIcon();
            _idle?.NotifyActivity();
            return;
        }

        var hit = _scene.NodeAt(_pointer.X, _pointer.Y);
        if (_openMenu is { } menu && hit is { Node: { } menuNode } && menu.OwnsMenuNode(menuNode))
        {
            LeaveFrameHover(except: null);
            _seat.Pointer.NotifyClearFocus();
            _cursor.SetHover(null, overClient: false);
            _menuHovering = true;
            menu.MenuPointerMotion(hit.Value.X, hit.Value.Y);
            _cursor.ShowNamed("left_ptr");
        }
        else
        {
            if (_menuHovering)
            {
                _menuHovering = false;
                _openMenu?.MenuPointerLeave();
            }

            if (TryRingResize(_pointer.X, _pointer.Y, RingMargin, CornerZone, out var hoverEdges, out _))
            {
                LeaveFrameHover(except: null);
                _seat.Pointer.NotifyClearFocus();
                _cursor.SetHover(null, overClient: false);
                _cursor.ShowNamed(Basin.Shell.Xdg.ResizeRing.CursorFor(hoverEdges));
            }
            else if (hit?.Surface is { } surface)
            {
                LeaveFrameHover(except: null);
                _seat.Pointer.NotifyMotionAt(timeMs, surface, hit.Value.X, hit.Value.Y, _pointer.X, _pointer.Y);
                _cursor.SetHover(surface, overClient: true);
            }
            else
            {
                _seat.Pointer.NotifyClearFocus();
                _cursor.SetHover(null, overClient: false);
                if (hit is { Node: { } hoverNode } && _windows.FindFrame(hoverNode) is { } frameHover)
                {
                    LeaveFrameHover(except: frameHover.Frame);
                    _frameHover = frameHover;
                    var localX = _pointer.X - frameHover.Owner.Tree.X;
                    var localY = _pointer.Y - frameHover.Owner.Tree.Y;
                    frameHover.Frame.PointerMotion(localX, localY);
                    _cursor.ShowNamed(frameHover.Frame.CursorAt(localX, localY) ?? "left_ptr");
                }
                else
                {
                    LeaveFrameHover(except: null);
                    _cursor.ShowNamed("left_ptr");
                }
            }
        }

        if (dx != 0 || dy != 0)
        {
            _relativePointer?.NotifyMotion(
                (ulong)timeMs * 1000, dx, dy, unaccelDx ?? dx, unaccelDy ?? dy);
        }

        PositionDragIcon();
        _idle?.NotifyActivity();
    }

    private void OnButton(uint timeMs, uint button, bool pressed)
    {
        _idle?.NotifyActivity();
        if (_mode != DragMode.None)
        {
            EndDrag();
            if (!pressed)
            {
                _seat.Pointer.NotifyButton(timeMs, button, pressed: false);
                _seat.Pointer.NotifyFrame();
                ProcessCursorMotion(timeMs);
                return;
            }

            ProcessCursorMotion(timeMs);
        }

        if (_openMenu is { } menu)
        {
            var menuHit = _scene.NodeAt(_pointer.X, _pointer.Y);
            if (menuHit is { Node: { } menuNode } && menu.OwnsMenuNode(menuNode))
            {
                if (button == InputCodes.BtnLeft)
                {
                    menu.MenuPointerButton(menuHit.Value.X, menuHit.Value.Y, pressed);
                    if (!menu.IsMenuOpen)
                    {
                        _openMenu = null;
                        _menuHovering = false;
                    }
                }

                return;
            }

            if (pressed)
            {
                DismissOpenMenu();
            }
        }

        if (button == InputCodes.BtnLeft && !pressed && _framePress is { } held)
        {
            _framePress = null;
            PrepareMenu(held);
            held.Frame.PointerButton(
                _pointer.X - held.Owner.Tree.X, _pointer.Y - held.Owner.Tree.Y, pressed: false, timeMs);
            if (held.Frame.IsMenuOpen)
            {
                _openMenu = held.Frame;
            }

            return;
        }

        if (pressed && button == InputCodes.BtnLeft && !_seat.Pointer.HasGrab &&
            TryRingResize(_pointer.X, _pointer.Y, RingMargin, CornerZone, out var ringEdges, out var ringView) &&
            ringView is not null)
        {
            ringView.Tree.RaiseToTop();
            _windows.Focus(ringView);
            BeginResize(ringView, ringEdges, serial: null);
            return;
        }

        if (pressed && button == InputCodes.BtnLeft && !_seat.Pointer.HasGrab &&
            _scene.NodeAt(_pointer.X, _pointer.Y) is { Node: { } frameNode } &&
            _windows.FindFrame(frameNode) is { } frameHit &&
            frameHit.Frame.PartAt(
                _pointer.X - frameHit.Owner.Tree.X, _pointer.Y - frameHit.Owner.Tree.Y) != FramePart.None)
        {
            frameHit.Owner.Tree.RaiseToTop();
            _windows.Focus(frameHit.Owner);
            _framePress = frameHit;
            PrepareMenu(frameHit);
            frameHit.Frame.PointerButton(
                _pointer.X - frameHit.Owner.Tree.X, _pointer.Y - frameHit.Owner.Tree.Y, pressed: true, timeMs);
            return;
        }

        if (pressed && button == InputCodes.BtnRight && !_seat.Pointer.HasGrab &&
            _scene.NodeAt(_pointer.X, _pointer.Y) is { Node: { } rightNode } &&
            _windows.FindFrame(rightNode) is { } rightHit)
        {
            var localX = _pointer.X - rightHit.Owner.Tree.X;
            var localY = _pointer.Y - rightHit.Owner.Tree.Y;
            if (rightHit.Frame.PartAt(localX, localY) is FramePart.Title or FramePart.Menu)
            {
                rightHit.Owner.Tree.RaiseToTop();
                _windows.Focus(rightHit.Owner);
                PrepareMenu(rightHit);
                rightHit.Frame.OpenMenu(localX, localY);
                _openMenu = rightHit.Frame.IsMenuOpen ? rightHit.Frame : null;
            }

            return;
        }

        _seat.Pointer.NotifyButton(timeMs, button, pressed);
        _seat.Pointer.NotifyFrame();
        if (pressed)
        {
            _windows.FocusAt(_scene.SurfaceAt(_pointer.X, _pointer.Y)?.Surface);
        }
    }

    private void BeginMove(PlasmaHostView view, uint? serial)
    {
        if (view.Minimized)
        {
            return;
        }

        _mode = DragMode.Move;
        _grabView = view;
        var (x, y) = GrabPosition(serial);
        _grabX = x - view.Tree.X;
        _grabY = y - view.Tree.Y;
    }

    private void BeginResize(PlasmaHostView view, Basin.Shell.Xdg.ResizeEdges edges, uint? serial)
    {
        if (view.Minimized || view.Maximized)
        {
            return;
        }

        _mode = DragMode.Resize;
        _grabView = view;
        _grabEdges = edges;
        (_grabX, _grabY) = GrabPosition(serial);
        var (width, height) = view.GeometrySize();
        _grabStart = new Box(view.Tree.X, view.Tree.Y, width, height);
        _windows.SetResizing(view, true);
    }

    private (double X, double Y) GrabPosition(uint? serial)
    {
        if (_frameTouchSlot is { } slot && _touch.MoveResize.TryBeginContact(slot, out var frameX, out var frameY))
        {
            return (frameX, frameY);
        }

        if (_touch.MoveResize.TryBegin(serial, out var pointX, out var pointY))
        {
            return (pointX, pointY);
        }

        _seat.Pointer.NotifyClearFocus();
        return (_pointer.X, _pointer.Y);
    }

    private bool DragTo(double x, double y)
    {
        switch (_mode)
        {
            case DragMode.Move when _grabView is { } view:
                _windows.MoveView(view, (int)(x - _grabX), (int)(y - _grabY));
                return true;

            case DragMode.Resize when _grabView is { } view:
                var box = new Basin.Shell.Xdg.ResizeDrag(_grabEdges, _grabStart, _grabX, _grabY)
                    .BoxFor(x, y, view.Tree.X, view.Tree.Y);
                _windows.ResizeView(view, box.X, box.Y, box.Width, box.Height, _grabEdges);
                return true;

            default:
                return false;
        }
    }

    private void EndDrag()
    {
        if (_mode == DragMode.Resize && _grabView is { } view)
        {
            _windows.SetResizing(view, false);
        }

        _mode = DragMode.None;
        _grabView = null;
        _framePress = null;
        _touch.MoveResize.End();
    }

    bool ITouchChrome.TryPress(int id, uint timeMs, double x, double y)
    {
        var topFrame = _scene.NodeAt(x, y) is { Node: { } topNode } ? _windows.FindFrame(topNode) : null;
        if (topFrame is null && _scene.SurfaceAt(x, y) is not null)
        {
            return false;
        }

        if (_mode != DragMode.None || _touchFramePress is not null)
        {
            return true;
        }

        if (topFrame is { } frameHit)
        {
            frameHit.Owner.Tree.RaiseToTop();
            _windows.Focus(frameHit.Owner);
            PrepareMenu(frameHit);
            _touchFramePress = frameHit;
            _frameTouchSlot = id;
            frameHit.Frame.TouchDown(x - frameHit.Owner.Tree.X, y - frameHit.Owner.Tree.Y, id, timeMs);
            _frameTouchSlot = null;
            if (frameHit.Frame.IsMenuOpen)
            {
                _openMenu = frameHit.Frame;
            }

            return true;
        }

        if (TryRingResize(x, y, TouchRingMargin, TouchCornerZone, out var ringEdges, out var ringView) &&
            ringView is not null)
        {
            ringView.Tree.RaiseToTop();
            _windows.Focus(ringView);
            _frameTouchSlot = id;
            BeginResize(ringView, ringEdges, serial: null);
            _frameTouchSlot = null;
            return true;
        }

        return false;
    }

    void ITouchChrome.Motion(int id, uint timeMs, double x, double y)
    {
    }

    void ITouchChrome.Release(int id, uint timeMs, double x, double y)
    {
        if (_touchFramePress is { } held)
        {
            _touchFramePress = null;
            held.Frame.TouchUp(x - held.Owner.Tree.X, y - held.Owner.Tree.Y, id);
            if (held.Frame.IsMenuOpen)
            {
                _openMenu = held.Frame;
            }
        }
    }

    void ITouchChrome.Cancel()
    {
        if (_touchFramePress is { } held)
        {
            _touchFramePress = null;
            held.Frame.TouchCancel();
        }
    }

    void ITouchDragHandler.DragTo(double x, double y) => DragTo(x, y);

    void ITouchDragHandler.DragEnd(bool cancelled)
    {
        if (_touchFramePress is { } held)
        {
            _touchFramePress = null;
            held.Frame.TouchCancel();
        }

        EndDrag();
    }

    private bool TryRingResize(
        double x, double y, int margin, int corner, out Basin.Shell.Xdg.ResizeEdges edges, out PlasmaHostView? view)
    {
        (edges, view) = (Basin.Shell.Xdg.ResizeEdges.None, null);
        PlasmaHostView? blocker = null;
        if (_scene.NodeAt(x, y) is { Node: { } node })
        {
            if (_windows.IsAboveWindows(node))
            {
                return false;
            }

            blocker = _windows.FindOwner(node);
        }

        foreach (var candidate in _windows.Views)
        {
            if (ReferenceEquals(candidate, blocker))
            {
                return false;
            }

            if (candidate.Frame is null || candidate.Minimized)
            {
                continue;
            }

            var candidateEdges =
                Basin.Shell.Xdg.ResizeRing.EdgesAt(_windows.FrameBoxOf(candidate), x, y, margin, corner);
            if (candidateEdges != Basin.Shell.Xdg.ResizeEdges.None)
            {
                (edges, view) = (candidateEdges, candidate);
                return true;
            }
        }

        return false;
    }

    private void PrepareMenu((Basin.Scene.Frame Frame, PlasmaHostView Owner) hit)
    {
        hit.Frame.MenuOrigin = new Point(hit.Owner.Tree.X, hit.Owner.Tree.Y);
        var output = _layout.OutputAt(_pointer.X, _pointer.Y);
        hit.Frame.MenuConstraint = output is null ? _layout.Bounds : _layout.BoxOf(output);
    }

    private void DismissOpenMenu()
    {
        _openMenu?.DismissMenu();
        _openMenu = null;
        _menuHovering = false;
    }

    private void LeaveFrameHover(Basin.Scene.Frame? except)
    {
        if (_frameHover is { } hover && hover.Frame != except)
        {
            hover.Frame.PointerLeave();
            _frameHover = null;
        }
    }

    private void OnAxis(uint timeMs, PointerAxis axis)
    {
        _seat.Pointer.NotifyAxis(timeMs, axis);
        _seat.Pointer.NotifyFrame();
        _idle?.NotifyActivity();
    }

    private void PositionDragIcon()
    {
        if (_dragIcon is { IsDestroyed: false } icon)
        {
            icon.Tree.SetPosition((int)_pointer.X, (int)_pointer.Y);
        }
    }

    void ITouchPointerTarget.Warp(uint timeMs, double x, double y)
    {
        _pointer.Warp(x, y);
        ProcessCursorMotion(timeMs);
    }

    void ITouchPointerTarget.Button(uint timeMs, uint button, bool pressed) =>
        OnButton(timeMs, button, pressed);

    void ITouchActivitySink.OnTouchActivity()
    {
        PositionDragIcon();
        _idle?.NotifyActivity();
    }

    public void Dispose()
    {
        _cursor.Dispose();
        _libinput?.Dispose();
        _session?.Dispose();
    }
}
