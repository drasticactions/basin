using Basin.Capabilities;
using Basin.Scene;
using Basin.Seat;

namespace Basin.Shell.Nested;

public sealed partial class NestedShell
{
    private const int TouchCapacity = TouchContacts.Capacity;
    private const long LongPressMillis = 500;
    private const double LongPressSlop = 8;

    private enum TouchOwner : byte
    {
        None,
        Gesture,
        Ui,
        Frame,
        Client,
        Pointer,
        Menu,
        Dead,
    }

    private struct TouchSlot
    {
        public int Id;
        public bool Live;
        public TouchOwner Owner;
        public double X;
        public double Y;
        public double DownX;
        public double DownY;
        public long DownAt;
        public bool LongPressArmed;
        public Frame? Frame;
        public ManagedWindow? Window;
        public SceneNode? Node;
    }

    private readonly TouchSlot[] _touches = new TouchSlot[TouchCapacity];
    private readonly TouchContacts _gestureContacts = new();
    private bool _gestureGrabbed;

    public bool TouchGestureActive => _gestureGrabbed;

    private void OnTouchDown(int id, double x, double y, uint time)
    {
        var slot = FindTouch(id);
        if (slot < 0)
        {
            slot = FreeTouch();
            if (slot < 0)
            {
                return;
            }
        }

        ref var touch = ref _touches[slot];
        touch = default;
        touch.Id = id;
        touch.Live = true;
        touch.X = x;
        touch.Y = y;
        touch.DownX = x;
        touch.DownY = y;
        touch.DownAt = Environment.TickCount64;

        if (_gestureGrabbed && _mode is GrabMode.Move or GrabMode.Resize)
        {
            touch.Owner = TouchOwner.Gesture;
            _gestureContacts.Down(id, x, y);
            return;
        }

        if (_mode != GrabMode.None)
        {
            EndGrab();
            touch.Owner = TouchOwner.Dead;
            return;
        }

        if (_uiPopups.Count > 0 && !WithinOpenPopups(_router.SurfaceAt(x, y)?.Surface))
        {
            PopupDismissRequested?.Invoke();
            touch.Owner = TouchOwner.Dead;
            return;
        }

        if (_openMenu is { } menu)
        {
            var menuHit = _host.Scene.NodeAt(x, y);
            if (menuHit is { Node: { } menuNode } && menu.OwnsMenuNode(menuNode))
            {
                touch.Owner = TouchOwner.Menu;
                touch.Frame = menu;
                menu.MenuPointerMotion(menuHit.Value.X, menuHit.Value.Y);
                return;
            }

            DismissOpenMenu();
        }

        if (_router.TouchDown(time, id, x, y))
        {
            touch.Owner = TouchOwner.Ui;
            if (_router.SurfaceAt(x, y) is { } ui && ChromeWindowOf(ui.Surface) is { } chrome)
            {
                if (!chrome.Active)
                {
                    Focus(chrome);
                }

                if (_settings.RaiseOnClick)
                {
                    Raise(chrome);
                }
            }

            return;
        }

        var hit = _host.Scene.NodeAt(x, y);
        if (hit is { Node: { } frameNode } && hit.Value.Surface is null && FindFrame(frameNode) is { } frameHit)
        {
            OnFrameTouch(ref touch, frameHit.Frame, frameHit.Owner, x, y, time);
            return;
        }

        if (hit?.Surface is { } surface)
        {
            if (WindowOf(surface) is { } window)
            {
                if (!window.Active)
                {
                    Focus(window);
                }

                if (_settings.RaiseOnClick)
                {
                    Raise(window);
                }
            }

            if (_host.Seat.Touch.Accepts(surface))
            {
                touch.Owner = TouchOwner.Client;
                touch.Node = hit.Value.Node;
                touch.LongPressArmed = true;
                _host.Seat.Touch.NotifyDown(surface, time, id, hit.Value.X, hit.Value.Y);
                _host.Seat.Touch.NotifyFrame();
                return;
            }

            if (_touchPointer.TryClaim(id, surface, time, x, y))
            {
                touch.Owner = TouchOwner.Pointer;
                touch.LongPressArmed = true;
                return;
            }

            touch.Owner = TouchOwner.Dead;
            return;
        }

        if (_focused is not null)
        {
            Focus(null);
        }

        if (_touchPointer.TryClaim(id, null, time, x, y))
        {
            touch.Owner = TouchOwner.Pointer;
            touch.LongPressArmed = true;
            return;
        }

        touch.Owner = TouchOwner.Dead;
    }

    private void OnFrameTouch(ref TouchSlot touch, Frame frame, ManagedWindow owner, double x, double y, uint time)
    {
        var localX = x - owner.X;
        var localY = y - owner.Y;
        var part = frame.PartAt(localX, localY);
        if (part == FramePart.None)
        {
            touch.Owner = TouchOwner.Dead;
            return;
        }

        Focus(owner);
        if (_settings.RaiseOnClick)
        {
            Raise(owner);
        }

        _cursorX = x;
        _cursorY = y;
        touch.Frame = frame;
        touch.Window = owner;
        touch.LongPressArmed = part is FramePart.Title or FramePart.Icon or FramePart.Border;
        PrepareMenu(owner, frame);
        frame.TouchDown(localX, localY, touch.Id, time);
        if (_mode is GrabMode.Move or GrabMode.Resize && ReferenceEquals(_grabWindow, owner))
        {
            BeginTouchGesture(ref touch);
            return;
        }

        touch.Owner = TouchOwner.Frame;
    }

    private void BeginTouchGesture(ref TouchSlot touch)
    {
        touch.Owner = TouchOwner.Gesture;
        _gestureContacts.Clear();
        _gestureContacts.Down(touch.Id, touch.X, touch.Y);
        if (!_gestureGrabbed)
        {
            _gestureGrabbed = true;
            _host.Seat.Touch.StartGrab(_gestureGrab);
        }
    }

    private void EndTouchGesture()
    {
        if (!_gestureGrabbed)
        {
            return;
        }

        _gestureGrabbed = false;
        _host.Seat.Touch.EndGrab(_gestureGrab);
        _gestureContacts.Clear();
        for (var i = 0; i < TouchCapacity; i++)
        {
            if (_touches[i].Live && _touches[i].Owner == TouchOwner.Gesture)
            {
                _touches[i].Owner = TouchOwner.Dead;
                _touches[i].LongPressArmed = false;
            }
        }
    }

    private bool TryAdoptTouch(uint? serial, out int slot)
    {
        slot = -1;
        if (serial is not { } value || !_host.Seat.Touch.TryGetPointBySerial(value, out var id))
        {
            return false;
        }

        slot = FindTouch(id);
        if (slot < 0 || _touches[slot].Owner != TouchOwner.Client)
        {
            slot = -1;
            return false;
        }

        _host.Seat.Touch.NotifyCancel();
        _cursorX = _touches[slot].X;
        _cursorY = _touches[slot].Y;
        return true;
    }

    private void OnTouchMotion(int id, double x, double y, uint time)
    {
        var slot = FindTouch(id);
        if (slot < 0)
        {
            return;
        }

        ref var touch = ref _touches[slot];
        touch.X = x;
        touch.Y = y;
        if (touch.LongPressArmed &&
            (Math.Abs(x - touch.DownX) > LongPressSlop || Math.Abs(y - touch.DownY) > LongPressSlop))
        {
            touch.LongPressArmed = false;
        }

        switch (touch.Owner)
        {
            case TouchOwner.Gesture:
                if (_gestureContacts.Motion(id, x, y, out var dx, out var dy))
                {
                    _cursorX += dx;
                    _cursorY += dy;
                    DragTo(_cursorX, _cursorY);
                }

                break;
            case TouchOwner.Ui:
                _router.TouchMotion(time, id, x, y);
                break;
            case TouchOwner.Client:
                if (touch.Node is { IsDestroyed: false } node && node.TryMapSceneToLocal(x, y, out var localX, out var localY))
                {
                    _host.Seat.Touch.NotifyMotion(time, id, localX, localY);
                }
                else
                {
                    _host.Seat.Touch.NotifyUp(time, id);
                    touch.Owner = TouchOwner.Dead;
                    touch.Node = null;
                }

                _host.Seat.Touch.NotifyFrame();
                break;
            case TouchOwner.Pointer:
                _touchPointer.Motion(id, time, x, y);
                break;
            case TouchOwner.Menu:
                if (_openMenu is { } menu)
                {
                    var menuHit = _host.Scene.NodeAt(x, y);
                    if (menuHit is { Node: { } menuNode } && menu.OwnsMenuNode(menuNode))
                    {
                        menu.MenuPointerMotion(menuHit.Value.X, menuHit.Value.Y);
                    }
                    else
                    {
                        menu.MenuPointerLeave();
                    }
                }

                break;
        }
    }

    private void OnTouchUp(int id, uint time)
    {
        var slot = FindTouch(id);
        if (slot < 0)
        {
            return;
        }

        ref var touch = ref _touches[slot];
        touch.Live = false;
        touch.LongPressArmed = false;
        switch (touch.Owner)
        {
            case TouchOwner.Gesture:
                _gestureContacts.Up(id);
                if (_gestureContacts.Count == 0)
                {
                    EndGrab();
                }

                break;
            case TouchOwner.Ui:
                _router.TouchUp(time, id);
                break;
            case TouchOwner.Frame:
                if (touch.Frame is { } frame && touch.Window is { } owner)
                {
                    PrepareMenu(owner, frame);
                    frame.TouchUp(touch.X - owner.X, touch.Y - owner.Y, id);
                    if (frame.IsMenuOpen)
                    {
                        _openMenu = frame;
                        _menuOwner = owner;
                    }
                }

                break;
            case TouchOwner.Client:
                _host.Seat.Touch.NotifyUp(time, id);
                _host.Seat.Touch.NotifyFrame();
                break;
            case TouchOwner.Pointer:
                _touchPointer.Release(id, time);
                break;
            case TouchOwner.Menu:
                if (_openMenu is { } menu)
                {
                    var menuHit = _host.Scene.NodeAt(touch.X, touch.Y);
                    if (menuHit is { Node: { } menuNode } && menu.OwnsMenuNode(menuNode))
                    {
                        menu.MenuPointerButton(menuHit.Value.X, menuHit.Value.Y, pressed: false);
                    }
                    else
                    {
                        DismissOpenMenu();
                    }

                    if (!menu.IsMenuOpen)
                    {
                        _openMenu = null;
                        _menuOwner = null;
                        _menuHovering = false;
                    }
                }

                break;
        }

        touch.Frame = null;
        touch.Window = null;
        touch.Node = null;
    }

    private void OnTouchCancel()
    {
        _host.CancelTouch();
        _touchPointer.Cancel();
        _router.TouchCancel();
        for (var i = 0; i < TouchCapacity; i++)
        {
            ref var touch = ref _touches[i];
            if (touch.Live && touch.Owner == TouchOwner.Frame)
            {
                touch.Frame?.TouchCancel();
            }

            touch = default;
        }

        if (_gestureGrabbed)
        {
            CancelGrab();
        }
    }

    private void TickLongPress(long nowMillis)
    {
        for (var i = 0; i < TouchCapacity; i++)
        {
            ref var touch = ref _touches[i];
            if (!touch.Live || !touch.LongPressArmed || nowMillis - touch.DownAt < LongPressMillis)
            {
                continue;
            }

            touch.LongPressArmed = false;
            FireLongPress(ref touch, unchecked((uint)nowMillis));
        }
    }

    private void FireLongPress(ref TouchSlot touch, uint time)
    {
        switch (touch.Owner)
        {
            case TouchOwner.Gesture:
            case TouchOwner.Frame:
                if (touch.Frame is not { } frame || touch.Window is not { } owner)
                {
                    return;
                }

                if (touch.Owner == TouchOwner.Gesture)
                {
                    CancelGrab();
                }
                else
                {
                    frame.TouchCancel();
                }

                touch.Owner = TouchOwner.Dead;
                RunTitlebarAction(owner, _settings.RightClickTitlebar, touch.X - owner.X, touch.Y - owner.Y);
                break;
            case TouchOwner.Client:
                _host.Seat.Touch.NotifyCancel();
                touch.Owner = TouchOwner.Dead;
                touch.Node = null;
                RightClickAt(touch.X, touch.Y, time);
                break;
            case TouchOwner.Pointer:
                _touchPointer.Release(touch.Id, time);
                touch.Owner = TouchOwner.Dead;
                RightClickAt(touch.X, touch.Y, time);
                break;
        }
    }

    private void RightClickAt(double x, double y, uint time)
    {
        MoveCursor(x, y, time);
        OnButton(time, BtnRight, pressed: true);
        OnButton(time, BtnRight, pressed: false);
    }

    private int FindTouch(int id)
    {
        for (var i = 0; i < TouchCapacity; i++)
        {
            if (_touches[i].Live && _touches[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private int FreeTouch()
    {
        for (var i = 0; i < TouchCapacity; i++)
        {
            if (!_touches[i].Live)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class TouchPointerTarget(NestedShell shell) : ITouchPointerTarget
    {
        public void Warp(uint timeMs, double x, double y) => shell.MoveCursor(x, y, timeMs);

        public void Button(uint timeMs, uint button, bool pressed) => shell.OnButton(timeMs, button, pressed);
    }

    private sealed class TouchGestureGrab(NestedShell shell) : ITouchGrab
    {
        public uint Down(Surface surface, uint timeMs, int id, double x, double y) => 0;

        public void Up(uint timeMs, int id)
        {
        }

        public void Motion(uint timeMs, int id, double x, double y)
        {
        }

        public void Frame()
        {
        }

        public void Cancel()
        {
            shell.CancelGrab();
            shell._host.Seat.Touch.SendCancel();
        }
    }
}
