using System.Diagnostics;
using Basin;
using Basin.Backend.Libinput;
using Basin.Cli;
using Basin.Effects;
using Basin.Backend.Wayland;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Capabilities;
using Basin.UI.Skia;
using Wayland;
using Wayland.Server;

using Basin.Diagnostics;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private void WireTablets()
    {
        var tablets = _services.Require<Basin.Desktop.TabletManager>();
        tablets.ToolProximityIn += (tool, _, axes) => AimTool(tool, axes);
        tablets.ToolMoved += AimTool;
    }

    private void AimTool(Basin.Desktop.TabletManager.TabletTool tool, Basin.Capabilities.TabletToolAxes axes)
    {
        if (_views.Count == 0)
        {
            return;
        }

        _idle.NotifyActivity();
        Basin.Desktop.TabletAiming.AimAt(tool, _scene, _layout, _cursor.CursorOutput ?? _views[0].Output, axes);
    }

    private void WireLibinput(Basin.Backend.Libinput.LibinputBackend input)
    {
        input.SwitchToggled += (_, switchEvent) =>
        {
            if (switchEvent.Switch == Libinput.LibinputSwitchType.TabletMode &&
                _layoutConfiguration is { } layoutConfiguration)
            {
                layoutConfiguration.TabletMode = switchEvent.State == Libinput.LibinputSwitchState.On;
            }
        };

        input.DeviceAdded += device =>
        {
            BasinReport.Line($"INPUT + {device.Name}");
            ConfigureTouchpad(device);
        };
        input.DeviceRemoved += device => BasinReport.Line($"INPUT - {device.Name}");
        _touchBinder!.Key += (time, key, pressed) =>
        {
            _idle.NotifyActivity();
            HandleKey(time, key, pressed);
        };
        _touchBinder!.Button += OnButton;
        _touchBinder!.Motion += (time, dx, dy, dxu, dyu) =>
        {
            _idle.NotifyActivity();
            if (dxu is { } unacceleratedDx && dyu is { } unacceleratedDy)
            {
                _relativePointer.NotifyMotion((ulong)time * 1000, dx, dy, unacceleratedDx, unacceleratedDy);
            }

            if (ActiveLock() is not null)
            {
                return;
            }

            OnPointerPlaced(time);
        };
        _touchBinder!.Axis += (time, axis) => _seat.Pointer.NotifyAxis(time, axis);
        input.Gesture += (_, type, gesture) =>
        {
            _idle.NotifyActivity();
            var time = (uint)(gesture.TimestampMicroseconds / 1000);
            switch (type)
            {
                case Libinput.LibinputEventType.GestureSwipeBegin:
                    if (!BeginWorkspaceSwipe((uint)gesture.FingerCount, time))
                    {
                        _gestures.NotifySwipeBegin(time, (uint)gesture.FingerCount);
                    }

                    break;
                case Libinput.LibinputEventType.GestureSwipeUpdate:
                    if (!UpdateWorkspaceSwipe(gesture.Dx, gesture.Dy, time))
                    {
                        _gestures.NotifySwipeUpdate(time, gesture.Dx, gesture.Dy);
                    }

                    break;
                case Libinput.LibinputEventType.GestureSwipeEnd:
                    if (!EndWorkspaceSwipe(gesture.Cancelled, time))
                    {
                        _gestures.NotifySwipeEnd(time, gesture.Cancelled);
                    }

                    break;
                case Libinput.LibinputEventType.GesturePinchBegin:
                    _gestures.NotifyPinchBegin(time, (uint)gesture.FingerCount);
                    break;
                case Libinput.LibinputEventType.GesturePinchUpdate:
                    _gestures.NotifyPinchUpdate(time, gesture.Dx, gesture.Dy, gesture.Scale, gesture.AngleDelta);
                    break;
                case Libinput.LibinputEventType.GesturePinchEnd:
                    _gestures.NotifyPinchEnd(time, gesture.Cancelled);
                    break;
                case Libinput.LibinputEventType.GestureHoldBegin:
                    _gestures.NotifyHoldBegin(time, (uint)gesture.FingerCount);
                    break;
                case Libinput.LibinputEventType.GestureHoldEnd:
                    _gestures.NotifyHoldEnd(time, gesture.Cancelled);
                    break;
            }
        };
    }

    private static void ConfigureTouchpad(Basin.Backend.Libinput.InputDevice device)
    {
        var config = device.Config;
        if (config.Tap.FingerCount > 0)
        {
            config.Tap.Enabled = true;
        }

        if (config.Click.Methods.HasFlag(Libinput.LibinputClickMethod.Clickfinger))
        {
            config.Click.Method = Libinput.LibinputClickMethod.Clickfinger;
        }
    }

    private const int TouchGripSlop = 12;
    private const int TouchRingMargin = 32;
    private const int TouchCornerZone = 40;
    private const int TouchSplitGrabZone = 16;

    private readonly Basin.Seat.CentroidSwipeGesture _touchSwipeGesture = new()
    {
        Fingers = TouchSwipeFingers,
        Slop = TouchSwipeSlop,
    };

    private Basin.Seat.Backends.SeatBinder? _touchBinder;
    private Basin.Seat.Backends.SeatTouchDriver? _touchDriver;
    private Basin.Seat.TouchMoveResize? _touchMoveResize;
    private readonly Basin.Shell.Xdg.GrabOrigin _grabOrigin;
    private (Frame Frame, IGrabTarget Owner)? _touchFramePress;

    private void SetupTouch()
    {
        _touchBinder = new Basin.Seat.Backends.SeatBinder(
            _seat, _layout, _pointer ?? new LayoutPointer(_layout), _cursor)
        {
            Drm = _drm,
            Theme = _cursorTheme,
        };
        _touchDriver = new Basin.Seat.Backends.SeatTouchDriver(_touchBinder, _seat);
        _touchMoveResize = _touchDriver.MoveResize;
        _grabOrigin.Touch = _touchMoveResize;
        _touchMoveResize.Handler = this;
        _touchSwipeGesture.Handler = this;
        _touchDriver.Router.HitTester = new Basin.Seat.Backends.SceneTouchHitTester(_scene);
        _touchDriver.Router.Chrome = this;
        _touchDriver.Router.Gestures = _touchSwipeGesture;
        _touchDriver.Router.Activity = this;
        _touchDriver.AttachPointer(this);
        _touchDriver.Routed += (_, _, surface) =>
        {
            if (surface is not null)
            {
                FocusSurfaceOwner(surface);
            }
        };
    }

    void Basin.Seat.ITouchActivitySink.OnTouchActivity() => _idle.NotifyActivity();

    void Basin.Seat.ITouchPointerTarget.Warp(uint timeMs, double x, double y) => MoveCursor(x, y, timeMs);

    void Basin.Seat.ITouchPointerTarget.Button(uint timeMs, uint button, bool pressed) =>
        OnButton(timeMs, button, pressed);

    void Basin.Seat.ITouchDragHandler.DragTo(double x, double y) => DragTo(x, y);

    void Basin.Seat.ITouchDragHandler.DragEnd(bool cancelled) => EndTouchDrag();

    bool Basin.Seat.ITouchChrome.TryPress(int id, uint timeMs, double x, double y)
    {
        _feedback?.OnTouchDown(id, x, y, EffectTick());
        var topFrame = _scene.NodeAt(x, y) is { Node: { } topNode } ? FindFrame(topNode) : null;
        if (topFrame is null && _scene.SurfaceAt(x, y) is not null)
        {
            return false;
        }

        if (_mode != DragMode.None || _touchFramePress is not null)
        {
            return true;
        }

        if (ViewAt(x, y)?.Active is { Tiled.Count: 2 } tiled &&
            Math.Abs(x - SplitX(tiled)) <= TouchSplitGrabZone &&
            y >= tiled.TileArea.Y && y < tiled.TileArea.Bottom)
        {
            BeginSplitDrag(tiled);
            _ = _touchMoveResize!.TryBeginContact(id, out _, out _);
            return true;
        }

        if (topFrame is { } frameHit)
        {
            FocusFrameOwner(frameHit.Owner);
            PrepareMenu(frameHit);
            _touchFramePress = frameHit;
            _grabOrigin.FrameTouchSlot = id;
            frameHit.Frame.TouchDown(x - frameHit.Owner.X, y - frameHit.Owner.Y, id, timeMs);
            _grabOrigin.FrameTouchSlot = null;
            if (frameHit.Frame.IsMenuOpen)
            {
                _openMenu = frameHit.Frame;
            }

            return true;
        }

        if (TryRingResize(x, y, TouchRingMargin, TouchCornerZone, out var ringEdges, out var ringWindow, out var ringXWindow))
        {
            _grabOrigin.FrameTouchSlot = id;
            if (ringWindow is not null)
            {
                FocusWindow(ringWindow);
                BeginResize(ringWindow, ringEdges);
            }
            else if (ringXWindow is not null)
            {
                FocusXWindow(ringXWindow);
                BeginResize(ringXWindow, ringEdges);
            }

            _grabOrigin.FrameTouchSlot = null;
            return true;
        }

        return false;
    }

    void Basin.Seat.ITouchChrome.Motion(int id, uint timeMs, double x, double y) =>
        _feedback?.OnTouchMotion(id, x, y, EffectTick());

    void Basin.Seat.ITouchChrome.Release(int id, uint timeMs, double x, double y)
    {
        _feedback?.OnTouchUp(id, EffectTick());
        if (_touchFramePress is { } held)
        {
            _touchFramePress = null;
            held.Frame.TouchUp(x - held.Owner.X, y - held.Owner.Y, id);
            if (held.Frame.IsMenuOpen)
            {
                _openMenu = held.Frame;
            }
        }
    }

    void Basin.Seat.ITouchChrome.Cancel()
    {
        if (_touchFramePress is { } held)
        {
            _touchFramePress = null;
            held.Frame.TouchCancel();
        }
    }

    private void EndTouchDrag()
    {
        if (_touchFramePress is { } dragging)
        {
            _touchFramePress = null;
            dragging.Frame.TouchCancel();
        }

        if (_mode != DragMode.None)
        {
            if (_mode == DragMode.Split)
            {
                EndSplitDrag();
            }

            if (_mode == DragMode.Move && _grabWindow is { } dropped)
            {
                ReassignDraggedWorkspace(dropped);
            }

            _grabWindow?.SetResizing(false);
            _mode = DragMode.None;
            _effects.OnGrabEnd();
            _grabWindow = null;
        }
    }

    private (Frame Frame, IGrabTarget Owner)? FindFrame(SceneNode node)
    {
        foreach (var window in _windows)
        {
            if (window.Frame is { } frame && frame.OwnsNode(node))
            {
                return (frame, window);
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Frame is { } frame && frame.OwnsNode(node))
            {
                return (frame, xwindow);
            }
        }

        return null;
    }

    private void FocusFrameOwner(IGrabTarget owner)
    {
        if (owner is Window window)
        {
            FocusWindow(window);
        }
        else if (owner is XWindow xwindow)
        {
            FocusXWindow(xwindow);
        }
    }

    private void FocusSurfaceOwner(Surface surface)
    {
        foreach (var window in _windows)
        {
            if (window.Owns(surface))
            {
                FocusWindow(window);
                return;
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Framable && xwindow.XWin.Surface == surface)
            {
                FocusXWindow(xwindow);
                return;
            }
        }
    }

    private void OnPointerPlaced(uint time)
    {
        MoveCursor(_pointer!.X, _pointer.Y, time);
    }

    private void LoadCursorTheme()
    {
        _touchBinder!.EnsureCursorLoaded();
        BasinReport.Line($"CURSOR left_ptr {_cursor.Images?.Size ?? 0}px {_cursor.DrawnBy}");
    }

    internal void SetHostChromeCursor(string name) => _cursor.ShowNamed(name);

    private static bool IsTrusted(WlClient client)
    {
        if (Basin.Desktop.SecurityContextManager.ContextOf(client) is not null)
        {
            return false;
        }

        return client.TryGetCredentials(out var credentials) && credentials.Uid == OwnUid;
    }

    private static readonly uint OwnUid = GetUid();

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();
}
