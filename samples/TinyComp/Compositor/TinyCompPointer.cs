using System.Diagnostics;
using Basin;
using Basin.Host;
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
    private void WirePointer(WaylandPointerDevice pointer)
    {
        _cursor.AttachParent(pointer);

        pointer.Enter += (output, x, y) =>
        {
            var (layoutX, layoutY) = _layout.ToLayout(output, x, y);
            MoveCursor(layoutX, layoutY, (uint)Environment.TickCount);
        };
        pointer.Motion += (time, x, y) =>
        {
            var output = _layout.OutputAt(_cursorX, _cursorY) ?? Views[0].Output;
            var (layoutX, layoutY) = _layout.ToLayout(output, x, y);
            MoveCursor(layoutX, layoutY, time);
        };
        pointer.RelativeMotion += (time, dx, dy, dxu, dyu) =>
            _relativePointer.NotifyMotion(time, dx, dy, dxu, dyu);
        pointer.Button += (time, button, pressed) => OnButton(time, button, pressed);
        pointer.Axis += (time, axis) => _seat.Pointer.NotifyAxis(time, axis);
        pointer.Leave += () => _seat.Pointer.NotifyClearFocus();
        pointer.SwipeBegin += (time, fingers) =>
        {
            if (!BeginWorkspaceSwipe(fingers, time))
            {
                _gestures.NotifySwipeBegin(time, fingers);
            }
        };
        pointer.SwipeUpdate += (time, dx, dy) =>
        {
            if (!UpdateWorkspaceSwipe(dx, dy, time))
            {
                _gestures.NotifySwipeUpdate(time, dx, dy);
            }
        };
        pointer.SwipeEnd += (time, cancelled) =>
        {
            if (!EndWorkspaceSwipe(cancelled, time))
            {
                _gestures.NotifySwipeEnd(time, cancelled);
            }
        };
        pointer.PinchBegin += (time, fingers) => _gestures.NotifyPinchBegin(time, fingers);
        pointer.PinchUpdate += (time, dx, dy, scale, rotation) =>
            _gestures.NotifyPinchUpdate(time, dx, dy, scale, rotation);
        pointer.PinchEnd += (time, cancelled) => _gestures.NotifyPinchEnd(time, cancelled);
        pointer.HoldBegin += (time, fingers) => _gestures.NotifyHoldBegin(time, fingers);
        pointer.HoldEnd += (time, cancelled) => _gestures.NotifyHoldEnd(time, cancelled);
    }

    internal void InjectPointerMotion(uint time, double dx, double dy) =>
        MoveCursor(_cursorX + dx, _cursorY + dy, time);

    internal void InjectPointerMotionAbsolute(uint time, double x, double y)
    {
        var bounds = _layout.Bounds;
        MoveCursor(bounds.X + (x * bounds.Width), bounds.Y + (y * bounds.Height), time);
    }

    internal void InjectPointerButton(uint time, uint button, bool pressed) => OnButton(time, button, pressed);

    internal void InjectKey(uint time, uint key, bool pressed) =>
        HandleKey(time, key, pressed, fromInputMethod: true);

    private void MoveCursor(double x, double y, uint time)
    {
        var rawDx = x - _lastRawX;
        var rawDy = y - _lastRawY;
        (_lastRawX, _lastRawY) = (x, y);
        if (ActiveLock() is not null)
        {
            _relativePointer.NotifyMotion((ulong)time * 1000, rawDx, rawDy, rawDx, rawDy);
            return;
        }

        _cursorX = x;
        _cursorY = y;
        _cursor.MoveTo(x, y);
        _post.SetCursor(x, y, (long)time * 1_000_000);
        _feedback?.OnMotion(x, y, (long)time * 1_000_000, EffectTick());
        if (TraceEnabled)
        {
            Trace($"motion {x:F1},{y:F1} mode={_mode}");
        }

        if (_touchMoveResize is not { Dragging: true } && DragTo(x, y))
        {
            return;
        }

        UpdateHoverCursor(x, y);
        RouteMotion(time, x, y);
    }

    private void RefreshPointer()
    {
        if (_mode != DragMode.None || _touchMoveResize is { Dragging: true } || ActiveLock() is not null)
        {
            return;
        }

        UpdateHoverCursor(_cursorX, _cursorY);
        RouteMotion((uint)Environment.TickCount, _cursorX, _cursorY);
    }

    private bool DragTo(double x, double y)
    {
        switch (_mode)
        {
            case DragMode.Move when _grabWindow is { } window:
                var beforeX = window.X;
                var beforeY = window.Y;
                window.MoveTo((int)(x - _grabX), (int)(y - _grabY));
                _effects.OnMoved(window.X - beforeX, window.Y - beforeY);
                return true;

            case DragMode.Split:
                DragSplit(x);
                return true;

            case DragMode.Resize when _grabWindow is { } window:
                var box = new ResizeDrag(_grabEdges, _grabStart, _grabX, _grabY).BoxFor(x, y, window.X, window.Y);
                window.ResizeTo(box.X, box.Y, box.Width, box.Height, _grabEdges);
                return true;

            default:
                return false;
        }
    }

    private void RouteMotion(uint time, double x, double y)
    {
        _dragIcon.Follow();

        Window? dragged = null;
        if (_toplevelDrags.Attachment is { } attached && FindWindow(attached.Toplevel) is { Tree: not null } window)
        {
            var geometry = attached.Toplevel.Xdg.EffectiveGeometry;
            window.MoveTo((int)x - attached.OffsetX - geometry.X, (int)y - attached.OffsetY - geometry.Y);
            window.Tree!.Enabled = false;
            dragged = window;
        }

        var hit = _scene.SurfaceAt(x, y);
        if (dragged?.Tree is { } draggedTree)
        {
            draggedTree.Enabled = true;
        }

        _seat.Pointer.NotifyMotionAt(time, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, x, y);
        UpdateConstraint(hit?.Surface);
    }

    private Basin.Desktop.PointerConstraint? _activeConstraint;
    private WaylandOutput? _parentLockOutput;
    private Basin.Desktop.PointerConstraint? _parentLockConstraint;

    private Basin.Desktop.PointerConstraint? ActiveLock() =>
        _activeConstraint is { IsActive: true, Kind: Basin.Desktop.ConstraintKind.Lock } ? _activeConstraint : null;

    private void UpdateConstraint(Surface? focused)
    {
        var next = focused is null ? null : _constraints.ConstraintFor(focused);
        if (_activeConstraint == next)
        {
            return;
        }

        _activeConstraint?.Deactivate();
        _activeConstraint = next;
        next?.Activate();
        SyncParentPointerLock();
    }

    private void SyncParentPointerLock()
    {
        if (_backend is null)
        {
            return;
        }

        var wanted = ActiveLock() is null
            ? null
            : _layout.OutputAt(_cursorX, _cursorY) as WaylandOutput;
        if (ReferenceEquals(wanted, _parentLockOutput))
        {
            return;
        }

        if (_parentLockOutput is { } previous)
        {
            var (releaseX, releaseY) = ParentLockRelease();
            var box = _layout.BoxOf(previous);
            previous.SetCursorPositionHint(
                (releaseX - box.X) * previous.Scale,
                (releaseY - box.Y) * previous.Scale);
            previous.LockPointer(false);
            if (releaseX != _cursorX || releaseY != _cursorY)
            {
                MoveCursor(releaseX, releaseY, (uint)Environment.TickCount);
            }
        }

        _parentLockOutput = wanted;
        _parentLockConstraint = wanted is null ? null : ActiveLock();
        if (wanted is not null && !wanted.LockPointer(true))
        {
            _parentLockOutput = null;
            _parentLockConstraint = null;
        }
    }

    private void RaiseHostWindow(Window window)
    {
        if (_backend is null)
        {
            return;
        }

        var target = window.Workspace is { } workspace && ViewOf(workspace) is { } view
            ? view.Output as WaylandOutput
            : null;
        target ??= _backend.Outputs.Count > 0 ? _backend.Outputs[0] : null;
        target?.RequestActivation();
    }

    private (WaylandOutput Output, Box Rect)? LocateGuestCaret(Surface surface, Box rect)
    {
        _caretSurfaces.Clear();
        _scene.CollectSurfaces(_caretSurfaces);
        foreach (var entry in _caretSurfaces)
        {
            if (entry.Surface != surface)
            {
                continue;
            }

            var layoutX = entry.Box.X + rect.X;
            var layoutY = entry.Box.Y + rect.Y;
            if (_layout.OutputAt(layoutX, layoutY) is not WaylandOutput output)
            {
                return null;
            }

            var box = _layout.BoxOf(output);
            return (output, new Box(
                (int)Math.Round((layoutX - box.X) * output.Scale),
                (int)Math.Round((layoutY - box.Y) * output.Scale),
                (int)Math.Round(rect.Width * output.Scale),
                (int)Math.Round(rect.Height * output.Scale)));
        }

        return null;
    }

    private (double X, double Y) ParentLockRelease()
    {
        if (_parentLockConstraint?.CursorPositionHint is { } hint &&
            _scene.SurfaceAt(_cursorX, _cursorY) is { Surface: { } surface } at &&
            surface == _parentLockConstraint.Surface)
        {
            return (_cursorX - at.X + hint.X, _cursorY - at.Y + hint.Y);
        }

        return (_cursorX, _cursorY);
    }

    private void OnButton(uint time, uint button, bool pressed)
    {
        if (TraceEnabled)
        {
            Trace($"button {button} pressed={pressed} mode={_mode}");
        }

        _feedback?.OnButton(_cursorX, _cursorY, button, pressed, EffectTick());
        if (_feedback is { MarksEnabled: true } marking && button == InputCodes.BtnMiddle && IsAltDown())
        {
            if (pressed)
            {
                marking.BeginMark(_cursorX, _cursorY);
            }
            else
            {
                marking.EndMark();
            }

            return;
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
            _touchMoveResize?.End();
            _framePress = null;
            if (!pressed)
            {
                _seat.Pointer.NotifyButton(time, button, pressed: false);
                RouteMotion(time, _cursorX, _cursorY);
                return;
            }

            RouteMotion(time, _cursorX, _cursorY);
        }

        if (_openMenu is { } menu)
        {
            var menuHit = _scene.NodeAt(_cursorX, _cursorY);
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
            held.Frame.PointerButton(_cursorX - held.Owner.X, _cursorY - held.Owner.Y, pressed: false, time);
            if (held.Frame.IsMenuOpen)
            {
                _openMenu = held.Frame;
            }

            return;
        }

        if (pressed && button == InputCodes.BtnLeft && !_seat.Pointer.HasGrab &&
            CurrentWorkspace() is { Tiled.Count: 2 } tiledWorkspace &&
            Math.Abs(_cursorX - SplitX(tiledWorkspace)) <= SplitGrabZone &&
            _cursorY >= tiledWorkspace.TileArea.Y && _cursorY < tiledWorkspace.TileArea.Bottom)
        {
            BeginSplitDrag(tiledWorkspace);
            return;
        }

        if (pressed && button == InputCodes.BtnLeft && !_seat.Pointer.HasGrab &&
            _scene.NodeAt(_cursorX, _cursorY) is { Node: { } frameNode } &&
            FindFrame(frameNode) is { } frameHit &&
            frameHit.Frame.PartAt(_cursorX - frameHit.Owner.X, _cursorY - frameHit.Owner.Y) != FramePart.None)
        {
            FocusFrameOwner(frameHit.Owner);
            _framePress = frameHit;
            PrepareMenu(frameHit);
            frameHit.Frame.PointerButton(_cursorX - frameHit.Owner.X, _cursorY - frameHit.Owner.Y, pressed: true, time);
            return;
        }

        if (pressed && button == InputCodes.BtnRight && !_seat.Pointer.HasGrab &&
            _scene.NodeAt(_cursorX, _cursorY) is { Node: { } rightNode } &&
            FindFrame(rightNode) is { } rightHit)
        {
            var localX = _cursorX - rightHit.Owner.X;
            var localY = _cursorY - rightHit.Owner.Y;
            if (rightHit.Frame.PartAt(localX, localY) is FramePart.Title or FramePart.Icon)
            {
                FocusFrameOwner(rightHit.Owner);
                PrepareMenu(rightHit);
                rightHit.Frame.OpenMenu(localX, localY);
                _openMenu = rightHit.Frame.IsMenuOpen ? rightHit.Frame : null;
            }

            return;
        }

        if (pressed && button == InputCodes.BtnLeft && !_seat.Pointer.HasGrab &&
            _scene.NodeAt(_cursorX, _cursorY) is null &&
            TryRingResize(_cursorX, _cursorY, RingMargin, CornerZone, out var ringEdges, out var ringWindow, out var ringXWindow))
        {
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

            return;
        }

        if (pressed && button == InputCodes.BtnLeft && !_seat.Pointer.HasGrab &&
            _scene.SurfaceAt(_cursorX, _cursorY) is { Surface: { } surface })
        {
            foreach (var window in _windows)
            {
                if (window.Owns(surface))
                {
                    FocusWindow(window);
                    if (IsAltDown())
                    {
                        BeginMove(window);
                        return;
                    }

                    break;
                }
            }

            foreach (var xwindow in _xwindows)
            {
                if (xwindow.Framable && xwindow.XWin.Surface == surface)
                {
                    FocusXWindow(xwindow);
                    if (IsAltDown())
                    {
                        BeginMove(xwindow);
                        return;
                    }

                    break;
                }
            }
        }

        _seat.Pointer.NotifyButton(time, button, pressed);
    }

    private const int RingMargin = 16;

    private bool TryRingResize(double x, double y, int margin, int corner, out ResizeEdges edges, out Window? xdgWindow, out XWindow? xWindow)
    {
        foreach (var window in _windows)
        {
            if (window.Minimized)
            {
                continue;
            }

            if (ResizeRing.EdgesAt(window.FrameBox, x, y, margin, corner) is var e && e != ResizeEdges.None)
            {
                (edges, xdgWindow, xWindow) = (e, window, null);
                return true;
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (ResizeRing.EdgesAt(xwindow.FrameBox, x, y, margin, corner) is var e && e != ResizeEdges.None)
            {
                (edges, xdgWindow, xWindow) = (e, null, xwindow);
                return true;
            }
        }

        (edges, xdgWindow, xWindow) = (ResizeEdges.None, null, null);
        return false;
    }

    private void UpdateHoverCursor(double x, double y)
    {
        var hit = _scene.NodeAt(x, y);
        var surface = hit?.Surface;

        if (_openMenu is { } openMenu && hit is { Node: { } maybeMenu } && openMenu.OwnsMenuNode(maybeMenu))
        {
            LeaveFrameHover(except: null);
            _cursor.SetHover(surface, overClient: false);
            _menuHovering = true;
            openMenu.MenuPointerMotion(hit.Value.X, hit.Value.Y);
            _cursor.ShowNamed("left_ptr");
            return;
        }

        if (_menuHovering)
        {
            _menuHovering = false;
            _openMenu?.MenuPointerLeave();
        }

        if (surface is not null)
        {
            LeaveFrameHover(except: null);
            _cursor.SetHover(surface, overClient: true);
            return;
        }

        _cursor.SetHover(null, overClient: false);
        if (hit is { Node: { } hoverNode } && FindFrame(hoverNode) is { } frameHover)
        {
            LeaveFrameHover(except: frameHover.Frame);
            _frameHover = frameHover;
            var localX = x - frameHover.Owner.X;
            var localY = y - frameHover.Owner.Y;
            frameHover.Frame.PointerMotion(localX, localY);
            _cursor.ShowNamed(frameHover.Frame.CursorAt(localX, localY) ?? "left_ptr");
            return;
        }

        LeaveFrameHover(except: null);

        if (hit is null && TryRingResize(x, y, RingMargin, CornerZone, out var edges, out _, out _))
        {
            _cursor.ShowNamed(ResizeRing.CursorFor(edges));
            return;
        }

        _cursor.ShowNamed("left_ptr");
    }

    private (Frame Frame, IGrabTarget Owner)? _frameHover;
    private (Frame Frame, IGrabTarget Owner)? _framePress;

    private Frame? _openMenu;
    private bool _menuHovering;

    private void PrepareMenu((Frame Frame, IGrabTarget Owner) hit)
    {
        hit.Frame.MenuOrigin = new Point(hit.Owner.X, hit.Owner.Y);
        var output = _layout.OutputAt(_cursorX, _cursorY) ?? Views.FirstOrDefault()?.Output;
        hit.Frame.MenuConstraint = output is null ? default : _layout.BoxOf(output);
    }

    private void DismissOpenMenu()
    {
        _openMenu?.DismissMenu();
        _openMenu = null;
        _menuHovering = false;
    }

    private void LeaveFrameHover(Frame? except)
    {
        if (_frameHover is { } hover && hover.Frame != except)
        {
            hover.Frame.PointerLeave();
            _frameHover = null;
        }
    }

    internal double ScaleAt(double x, double y)
    {
        var view = Views.FirstOrDefault(v => _layout.OutputAt(x, y) == v.Output) ?? Views.FirstOrDefault();
        return view?.Output.Scale ?? 1.0;
    }

    internal double ScaleForBox(in Box box)
    {
        var best = 0.0;
        foreach (var view in Views)
        {
            if (!_layout.BoxOf(view.Output).Intersect(box).IsEmpty)
            {
                best = Math.Max(best, view.Output.Scale);
            }
        }

        return best > 0 ? best : ScaleAt(box.X, box.Y);
    }

    internal double ScaleForWindow(Window window) => ScaleForBox(window.ScaleBox);
}
