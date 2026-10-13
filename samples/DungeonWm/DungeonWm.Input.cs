using System.Numerics;
using Basin;
using Basin.Config;
using Basin.Diagnostics;
using Basin.Scene3D;
using Basin.Scene3D.Scene;
using Basin.Seat.Backends;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private const uint ButtonLeft = 272;
    private const uint KeyW = 17;
    private const uint KeyA = 30;
    private const uint KeyS = 31;
    private const uint KeyD = 32;
    private const uint KeySpace = 57;
    private const uint KeyLeftShift = 42;
    private const uint KeyRightShift = 54;

    private readonly HashSet<uint> _swallowed = [];
    private readonly HashSet<uint> _withheld = [];
    private bool _rotating;
    private uint _rotateKey;
    private LayoutPointer _pointer = null!;
    private SeatBinder _binder = null!;
    private SeatInjector _injector = null!;
    private DungeonWmBindings _bindings = null!;
    private bool _looking;
    private bool _resizing;
    private DungeonWmWindow? _resized;
    private (int Width, int Height) _resizeStart;
    private Vector3 _resizeAnchor;
    private Quaternion _resizeOrientation;
    private float _resizeMetersPerPixel;
    private (int X, int Y) _resizeEdges;
    private DungeonWmWindow? _plaqueHover;
    private DungeonWmWindow? _plaquePress;

    private void WireInput()
    {
        _bindings = new DungeonWmBindings(_settings.Bindings, _log);
        _pointer = new LayoutPointer(_layout);
        _binder = new SeatBinder(_seat, _layout, _pointer, _cursor)
        {
            PointerFrozen = () => _looking || _crosshair || Rotating,
            Drm = _host.Drm,
            RelativePointer = _services.Find<Basin.Desktop.RelativePointerManager>(),
            Constraints = _services.Find<Basin.Desktop.PointerConstraintDriver>(),
        };
        if (_binder.Constraints is { } constraints)
        {
            constraints.Allow = constraint => constraint.Kind == Basin.Desktop.ConstraintKind.Lock || _takeover is not null;
        }

        _binder.Key += OnKey;
        _binder.Motion += OnMotion;
        _binder.Button += OnButton;
        _binder.Axis += (time, axis) =>
        {
            if (axis.Axis == Wayland.WlPointer.Axis.VerticalScroll && PullAxis(axis.Value))
            {
                return;
            }

            _seat.Pointer.NotifyAxis(time, axis);
            _seat.Pointer.NotifyFrame();
        };
        _binder.PointerLeft += () => _seat.Pointer.NotifyClearFocus();
        if (_input is not null)
        {
            _binder.BindLibinput(_input);
            _binder.KeyboardFor = _ => null;
        }

        if (_host.Parent is { } parent)
        {
            _binder.BindParent(parent);
        }

        _injector = new SeatInjector(_binder, _seat, _layout, _pointer)
        {
            Moved = PointerMoved,
            MovedBy = (time, dx, dy) =>
            {
                if (Rotating)
                {
                    if (_crosshair)
                    {
                        Recenter();
                    }
                    else
                    {
                        _pointer.Warp(_pointer.X - dx, _pointer.Y - dy);
                    }

                    RotateHeld(dx, dy);
                    return;
                }

                if (_crosshair)
                {
                    Recenter();
                    CrosshairLook(dx, dy);
                    return;
                }

                PointerMoved(time);
            },
            DeliverButton = OnButton,
            DeliverKey = OnKey,
        };
        _seat.Pointer.MapToSurface = MapToSurface;
        _binder.EnsureCursorLoaded();
        if (_outputs.Views.Count > 0)
        {
            var box = _layout.BoxOf(_outputs.Views[0].Output);
            _pointer.Warp(box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
            _cursor.MoveTo(_pointer.X, _pointer.Y);
        }

        DefineReticles();
        SyncMode();
    }

    private void OnMotion(uint time, double dx, double dy, double? unacceleratedDx, double? unacceleratedDy)
    {
        if (Rotating)
        {
            RotateHeld(unacceleratedDx ?? dx, unacceleratedDy ?? dy);
            return;
        }

        if (_crosshair)
        {
            CrosshairLook(unacceleratedDx ?? dx, unacceleratedDy ?? dy);
            return;
        }

        if (_looking)
        {
            var sensitivity = _settings.LookSensitivity;
            _walker.Look(-(float)(unacceleratedDx ?? dx) * sensitivity, -(float)(unacceleratedDy ?? dy) * sensitivity);
            ScheduleAll();
            return;
        }

        PointerMoved(time);
    }

    private void PointerMoved(uint time)
    {
        if (_crosshair)
        {
            Recenter();
            FollowCrosshair();
            return;
        }

        _cursor.MoveTo(_pointer.X, _pointer.Y);
        if (UpdatePull())
        {
            return;
        }

        if (_resizing && _resized is { } window)
        {
            ContinueResize(window);
            return;
        }

        Route(time);
    }

    private void Route(uint time)
    {
        var x = _pointer.X;
        var y = _pointer.Y;
        if (_scene.SurfaceAt(x, y) is { Surface: { } flat } hit)
        {
            _seat.Pointer.NotifyMotionAt(time, flat, hit.X, hit.Y, x, y);
            _cursor.SetHover(flat, overClient: true);
            return;
        }

        if (_takeover is not null || _locked)
        {
            _seat.Pointer.NotifyClearFocus();
            _cursor.ShowNamed("default");
            return;
        }

        if (_seat.Pointer.HasImplicitGrab && _seat.Pointer.Focus is { } pinned &&
            MapToSurface(pinned, x, y, out var pinnedX, out var pinnedY))
        {
            _seat.Pointer.NotifyMotionAt(time, pinned, pinnedX, pinnedY, x, y);
            return;
        }

        var pick = PickAt(x, y);
        HoverPlaque(pick);
        if (pick.Kind == WorldHitKind.Surface && pick.Surface is { } surface)
        {
            _seat.Pointer.NotifyMotionAt(time, surface, pick.SurfaceX, pick.SurfaceY, x, y);
            _cursor.SetHover(surface, overClient: true);
            return;
        }

        if (pick.Kind is WorldHitKind.Frame or WorldHitKind.Plaque && pick.Window?.Tag is DungeonWmWindow)
        {
            _seat.Pointer.NotifyClearFocus();
            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed(pick.Kind == WorldHitKind.Plaque
                ? PlaqueAt(pick) == PlaqueButton.None ? "default" : "pointer"
                : FrameCursor(pick.Part));
            return;
        }

        _seat.Pointer.NotifyClearFocus();
        _cursor.SetHover(null, overClient: false);
        _cursor.ShowNamed("default");
    }

    private WorldHit PickAt(double x, double y)
    {
        if (ViewAt(x, y) is not { } view)
        {
            return WorldHit.Miss;
        }

        _worldOutputs.TryGetValue(view, out var output);
        var slice = _layout.BoxOf(view.Output);
        if (slice.IsEmpty || _layout.Bounds.IsEmpty)
        {
            return WorldHit.Miss;
        }

        var ray = _camera.ViewOf(_layout.Bounds, slice).RayAt(x, y);
        return _picker.Pick(ray, output?.Visible is { Rooms.Count: > 0 } visible ? visible : null);
    }

    private Basin.Host.OutputView? ViewAt(double x, double y)
    {
        var output = _layout.OutputAt(x, y);
        foreach (var view in _outputs.Views)
        {
            if (ReferenceEquals(view.Output, output))
            {
                return view;
            }
        }

        return _outputs.Views.Count > 0 ? _outputs.Views[0] : null;
    }

    private Basin.Host.OutputView? OutputUnderPointer() => ViewAt(_pointer.X, _pointer.Y);

    private bool MapToSurface(Surface surface, double x, double y, out double surfaceX, out double surfaceY)
    {
        if (_takeover is null && WorldSurfaceMapper.TryMap(_world.World, _camera, _layout, surface, x, y, out surfaceX, out surfaceY))
        {
            return true;
        }

        return _scene.TryMapToSurface(surface, x, y, out surfaceX, out surfaceY);
    }

    private void OnButton(uint time, uint button, bool pressed)
    {
        if (!pressed && _plaquePress is { } pressedWindow && button == ButtonLeft)
        {
            ReleasePlaque(pressedWindow);
            PointerMoved(time);
            return;
        }

        if (!pressed && _pull is not null && button == ButtonLeft)
        {
            EndPull();
            PointerMoved(time);
            return;
        }

        if (!pressed && _looking && button == ButtonLeft)
        {
            _looking = false;
            PointerMoved(time);
            return;
        }

        if (!pressed && _resizing && button == ButtonLeft)
        {
            EndResize();
            UpdateReticle();
            return;
        }

        if (_crosshair)
        {
            if (pressed)
            {
                CrosshairPress(button);
            }

            return;
        }

        if (pressed && !_seat.Pointer.HasImplicitGrab)
        {
            var x = _pointer.X;
            var y = _pointer.Y;
            if (_scene.SurfaceAt(x, y) is { Surface: { } flat })
            {
                if (WindowOf(flat) is { } flatWindow)
                {
                    Focus(flatWindow);
                }
            }
            else if (_takeover is null && !_locked)
            {
                var super = (DungeonWmBindings.Held(_seat.Keyboard) & Modifiers.Super) != 0;
                if (super && button == ButtonLeft && OnSuperPress(x, y))
                {
                    return;
                }

                var pick = PickAt(x, y);
                if (pick.Kind is WorldHitKind.Frame or WorldHitKind.Plaque && pick.Window?.Tag is DungeonWmWindow framed)
                {
                    Focus(framed);
                    if (button != ButtonLeft)
                    {
                        return;
                    }

                    if (pick.Kind == WorldHitKind.Plaque)
                    {
                        PressPlaque(framed, PlaqueAt(pick));
                    }
                    else if (IsGrip(pick) && framed.State == DungeonWmWindowState.Hung)
                    {
                        BeginResize(framed, pick);
                    }
                    else
                    {
                        BeginPull(pick);
                    }

                    return;
                }

                if (pick.Kind == WorldHitKind.Surface && pick.Surface is { } surface && WindowOf(surface) is { } window)
                {
                    var interact = _focused is null;
                    Focus(window);
                    if (interact)
                    {
                        OnInteract(window);
                    }

                    _seat.Pointer.NotifyMotionAt(time, surface, pick.SurfaceX, pick.SurfaceY, x, y);
                }
                else
                {
                    if (!(super && button == ButtonLeft))
                    {
                        Focus(null);
                        return;
                    }

                    _looking = true;
                    return;
                }
            }
        }

        _seat.Pointer.NotifyButton(time, button, pressed);
        _seat.Pointer.NotifyFrame();
    }

    private static bool IsGrip(in WorldHit pick) =>
        pick.Kind == WorldHitKind.Frame && pick.Window is not null && pick.Part is not (WorldFramePart.None or WorldFramePart.Face);

    private static (int X, int Y) Edges(WorldFramePart part) => part switch
    {
        WorldFramePart.Left => (-1, 0),
        WorldFramePart.Right => (1, 0),
        WorldFramePart.Top => (0, -1),
        WorldFramePart.Bottom => (0, 1),
        WorldFramePart.TopLeft => (-1, -1),
        WorldFramePart.TopRight => (1, -1),
        WorldFramePart.BottomLeft => (-1, 1),
        WorldFramePart.BottomRight => (1, 1),
        _ => (0, 0),
    };

    private static string FrameCursor(WorldFramePart part) => part switch
    {
        WorldFramePart.Left => "w-resize",
        WorldFramePart.Right => "e-resize",
        WorldFramePart.Top => "n-resize",
        WorldFramePart.Bottom => "s-resize",
        WorldFramePart.TopLeft => "nw-resize",
        WorldFramePart.TopRight => "ne-resize",
        WorldFramePart.BottomLeft => "sw-resize",
        WorldFramePart.BottomRight => "se-resize",
        _ => "grab",
    };

    private void BeginResize(DungeonWmWindow window, in WorldHit pick)
    {
        _resizing = true;
        _resized = window;
        _resizeStart = (window.World.SurfaceWidth, window.World.SurfaceHeight);
        _resizeAnchor = pick.Point;
        _resizeOrientation = window.World.Orientation;
        _resizeMetersPerPixel = window.World.MetersPerPixel;
        _resizeEdges = Edges(pick.Part);
        window.SetResizing(true);
        BasinReport.Line($"RESIZE begin {window.AppId} {pick.Part.ToString().ToLowerInvariant()}");
    }

    private void ContinueResize(DungeonWmWindow window)
    {
        if (_resizeMetersPerPixel <= 0 || !_camera.TryRayAt(_layout, _pointer.X, _pointer.Y, out var ray))
        {
            return;
        }

        var normal = Vector3.Transform(Vector3.UnitZ, _resizeOrientation);
        if (!ray.TryPlane(_resizeAnchor, normal, out var distance))
        {
            return;
        }

        var local = Vector3.Transform(ray.At(distance) - _resizeAnchor, Quaternion.Inverse(_resizeOrientation));
        var dx = local.X / _resizeMetersPerPixel;
        var dy = -local.Y / _resizeMetersPerPixel;
        var width = _resizeStart.Width + (int)(_resizeEdges.X * dx * 2);
        var height = _resizeStart.Height + (int)(_resizeEdges.Y * dy * 2);
        width = Math.Clamp(width, Math.Max(64, window.MinWidth), window.MaxWidth > 0 ? window.MaxWidth : 8192);
        height = Math.Clamp(height, Math.Max(48, window.MinHeight), window.MaxHeight > 0 ? window.MaxHeight : 8192);
        window.RequestSize(width, height);
    }

    private void EndResize()
    {
        if (_resized is { } window)
        {
            window.SetResizing(false);
            window.Toplevel?.RequestConfigure();
            BasinReport.Line($"RESIZE end {window.AppId} {window.World.SurfaceWidth}x{window.World.SurfaceHeight}");
        }

        _resizing = false;
        _resized = null;
    }

    private void OnKey(uint time, uint key, bool pressed)
    {
        if (!pressed && _rotating && key == _rotateKey)
        {
            _rotating = false;
            BasinReport.Line($"ROTATE end");
        }

        if (!pressed && _withheld.Remove(key))
        {
            _swallowed.Remove(key);
            _seat.Keyboard.NotifyKeyConsumed(key, pressed);
            return;
        }

        if (!pressed && _swallowed.Remove(key))
        {
            SetWalkKey(key, false);
            _seat.Keyboard.NotifyKey(time, key, pressed);
            return;
        }

        var held = DungeonWmBindings.Held(_seat.Keyboard);
        if (pressed && !_locked)
        {
            var symbol = _seat.Keyboard.RawKeysymFor(key).Value;
            if (_bindings.Match(symbol, held, _takeover is not null) is { } hotkey && !(LayerHoldsKeyboard && ReferenceEquals(hotkey, _layerOpener)) && Applies(hotkey))
            {
                _swallowed.Add(key);
                if (hotkey.Action is "use" or "rotate")
                {
                    _withheld.Add(key);
                }

                if (hotkey.Action == "rotate")
                {
                    _rotateKey = key;
                }

                if (hotkey.Command is { } command)
                {
                    Spawn(hotkey, command);
                }
                else
                {
                    RunAction(hotkey.Action!);
                }

                return;
            }

            var super = (held & Modifiers.Super) != 0;
            if (IsWalkKey(key) && _takeover is null && !LayerHoldsKeyboard && (super || _focused is null))
            {
                _swallowed.Add(key);
                SetWalkKey(key, true);
                ScheduleAll();
                return;
            }
        }

        if (key is KeyLeftShift or KeyRightShift)
        {
            _walker.Run = pressed;
        }

        _seat.Keyboard.NotifyKey(time, key, pressed);
    }

    private bool Applies(Basin.Config.Hotkey hotkey) => hotkey.Action is not { } action || action switch
    {
        "use" => _crosshair,
        "release" => _focused is not null,
        "rotate" or "square" => _pull is { Detached: true },
        _ => true,
    };

    private bool Rotating => _rotating && _pull is { Detached: true };

    private void RotateHeld(double dx, double dy)
    {
        var sensitivity = _settings.LookSensitivity;
        TurnHeld(-(float)dx * sensitivity, (float)dy * sensitivity, 0);
        ScheduleAll();
    }

    private static bool IsWalkKey(uint key) => key is KeyW or KeyA or KeyS or KeyD or KeySpace;

    private void SetWalkKey(uint key, bool down)
    {
        switch (key)
        {
            case KeyW:
                _walker.Forward = down;
                break;
            case KeyS:
                _walker.Back = down;
                break;
            case KeyA:
                _walker.Left = down;
                break;
            case KeyD:
                _walker.Right = down;
                break;
            case KeySpace when down:
                _walker.Jump();
                OnJump();
                break;
        }
    }

    private void RunAction(string action)
    {
        BasinReport.Line($"ACTION {action}");
        switch (action)
        {
            case "leave-takeover":
                LeaveTakeover();
                break;
            case "takeover" when _focused is { } focused:
                EnterTakeover(focused);
                break;
            case "close" when _focused is { } focused:
                focused.Close();
                break;
            case "next-room":
                StepRoom(1);
                break;
            case "previous-room":
                StepRoom(-1);
                break;
            case "use":
                if (UseAtCrosshair() is null)
                {
                    BasinReport.Line("USE none");
                }

                break;
            case "release":
                Release();
                break;
            case "rotate":
                _rotating = true;
                BasinReport.Line($"ROTATE begin");
                break;
            case "square":
                SquareHeld();
                break;
            case "look-reset":
                _camera.Pitch = 0;
                ScheduleAll();
                break;
        }
    }

    private void StepRoom(int direction)
    {
        var rooms = _world.Map.Rooms.Where(r => !r.Removed).ToList();
        if (rooms.Count == 0)
        {
            return;
        }

        var index = _workspaces.Active is { } active ? rooms.IndexOf(active) : 0;
        var next = rooms[((index + direction) % rooms.Count + rooms.Count) % rooms.Count];
        FlyToRoom(next);
    }

    private void FlyToRoom(MapRegion room)
    {
        if (_takeover is not null)
        {
            LeaveTakeover();
        }

        _walker.FlyTo(_world.EntranceOf(room, _settings.EyeHeight + _walker.Feet), _world.EntranceYaw(room));
        BasinReport.Line($"FLY room={room.Workspace + 1}");
        ScheduleAll();
    }

    private partial bool OnSuperPress(double x, double y);

    private partial void OnInteract(DungeonWmWindow window);

    private partial void OnJump();
}
