using Basin.Hosted;
using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Shell.Nested;

public sealed partial class NestedShell
{
    private enum GrabMode
    {
        None,
        Move,
        Resize,
        KeyboardMove,
        KeyboardResize,
    }

    private const uint BtnLeft = 0x110;
    private const uint BtnRight = 0x111;
    private const uint BtnMiddle = 0x112;

    private double _cursorX;
    private double _cursorY;
    private GrabMode _mode;
    private ManagedWindow? _grabWindow;
    private double _grabX;
    private double _grabY;
    private ResizeEdges _grabEdges;
    private Box _grabStart;
    private bool _grabKeepsTitle;
    private Frame? _openMenu;
    private ManagedWindow? _menuOwner;
    private bool _menuHovering;
    private (Frame Frame, ManagedWindow Owner)? _frameHover;
    private (Frame Frame, ManagedWindow Owner)? _framePress;
    private ShellModifiers _held;
    private readonly HashSet<uint> _pressedKeys = [];
    private string _cursorName = "left_ptr";
    private bool _overClient;
    private bool _switcherOpen;
    private IReadOnlyList<ManagedWindow> _switcherOrder = [];
    private int _switcherIndex;
    private bool _panelHasKeyboard;
    private readonly Dictionary<IUISurface, IUISurface> _uiPopups = [];
    private uint? _popupDismissButton;
    private Surface? _cursorSurface;
    private Action? _cursorCommitted;
    private long _lastTitlePress;
    private ManagedWindow? _lastTitleWindow;
    private ManagedWindow? _autoRaiseCandidate;
    private long _autoRaiseAt;

    public void HandleInput(in BasinViewInput input)
    {
        if (_disposed)
        {
            return;
        }

        switch (input.Kind)
        {
            case BasinViewInputKind.PointerEnter:
            case BasinViewInputKind.PointerMotion:
                MoveCursor(input.X, input.Y, input.TimeMs);
                break;
            case BasinViewInputKind.PointerLeave:
                LeaveFrameHover(null);
                _host.Seat.Pointer.NotifyClearFocus();
                _router.PointerLeave();
                break;
            case BasinViewInputKind.PointerButton:
                OnButton(input.TimeMs, input.Code, input.Pressed);
                break;
            case BasinViewInputKind.PointerAxis:
                OnAxis(input.TimeMs, input.DeltaX, input.DeltaY);
                break;
            case BasinViewInputKind.Key:
                HandleKey(input.TimeMs, input.Code, input.Pressed);
                break;
            case BasinViewInputKind.TouchDown:
                OnTouchDown(input.TouchId, input.X, input.Y, input.TimeMs);
                break;
            case BasinViewInputKind.TouchMotion:
                OnTouchMotion(input.TouchId, input.X, input.Y, input.TimeMs);
                break;
            case BasinViewInputKind.TouchUp:
                OnTouchUp(input.TouchId, input.TimeMs);
                break;
            case BasinViewInputKind.TouchCancel:
                OnTouchCancel();
                break;
            case BasinViewInputKind.FocusIn:
                ApplyKeyboardFocus();
                break;
            case BasinViewInputKind.FocusOut:
                ReleaseHeldKeys();
                if (_switcherOpen)
                {
                    EndSwitcher(commit: true);
                }

                _host.Seat.Keyboard.NotifyClearFocus();
                break;
        }
    }

    public void Tick(long nowMillis)
    {
        TickLongPress(nowMillis);
        if (_autoRaiseCandidate is { } candidate && nowMillis >= _autoRaiseAt)
        {
            _autoRaiseCandidate = null;
            if (ReferenceEquals(candidate, _focused) && candidate.IsMapped)
            {
                Raise(candidate);
            }
        }
    }

    private void MoveCursor(double x, double y, uint time)
    {
        _cursorX = x;
        _cursorY = y;
        if (DragTo(x, y))
        {
            return;
        }

        UpdateHover(x, y);
        RouteMotion(time, x, y);
    }

    private bool DragTo(double x, double y)
    {
        switch (_mode)
        {
            case GrabMode.Move when _grabWindow is { } moving:
                if (moving.Maximized || moving.Tile != TileEdge.None)
                {
                    var frame = moving.FrameBox;
                    if (Math.Abs(x - _grabStart.X - _grabX) > 8 || Math.Abs(y - _grabStart.Y - _grabY) > 8)
                    {
                        var restored = moving.Restore ?? frame;
                        var fraction = frame.Width > 0 ? (x - frame.X) / frame.Width : 0.5;
                        moving.Content.SetMaximized(false);
                        moving.Tile = TileEdge.None;
                        moving.Content.SetTiled(ResizeEdges.None);
                        moving.Restore = null;
                        var insets = moving.Insets;
                        moving.Content.SetSize(
                            Math.Max(1, restored.Width - insets.Left - insets.Right),
                            Math.Max(1, restored.Height - insets.Top - insets.Bottom));
                        _grabX = restored.Width * fraction;
                        _grabY = Math.Min(_grabY, insets.Top);
                        DragFrameTo(moving, x, y);
                        _grabStart = moving.FrameBox;
                        moving.RefreshFrame();
                    }

                    return true;
                }

                DragFrameTo(moving, x, y);
                return true;
            case GrabMode.Resize when _grabWindow is { } resizing:
                var box = new ResizeDrag(_grabEdges, _grabStart, _grabX, _grabY).BoxFor(
                    x, y, _grabStart.X, _grabStart.Y,
                    Math.Max(32, resizing.Content.MinWidth), Math.Max(32, resizing.Content.MinHeight));
                var (width, height) = Constraints.ClampSize(
                    box.Width, box.Height, resizing.Content.MinWidth, resizing.Content.MinHeight,
                    resizing.Content.MaxWidth, resizing.Content.MaxHeight);
                box = box with { Width = width, Height = height };
                if (_grabKeepsTitle)
                {
                    box = ClipResizeToWorkArea(resizing, box);
                }

                resizing.ResizeClientTo(box, _grabEdges);
                return true;
            default:
                return false;
        }
    }

    private void RouteMotion(uint time, double x, double y)
    {
        if (_openMenu is not null && _host.Scene.NodeAt(x, y) is { Node: { } menuNode } menuHit && _openMenu.OwnsMenuNode(menuNode))
        {
            _menuHovering = true;
            _openMenu.MenuPointerMotion(menuHit.X, menuHit.Y);
            _host.Seat.Pointer.NotifyClearFocus();
            return;
        }

        if (_menuHovering)
        {
            _menuHovering = false;
            _openMenu?.MenuPointerLeave();
        }

        if (_router.PointerMotion(time, x, y).Surface is not null)
        {
            _host.Seat.Pointer.NotifyClearFocus();
            return;
        }

        var hit = _host.Scene.SurfaceAt(x, y);
        _host.Seat.Pointer.NotifyMotionAt(time, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, x, y);
        if (hit?.Surface is { } surface && FocusPolicy.FocusFollowsEnter(_settings.FocusMode)
            && WindowOf(surface) is { } under && !ReferenceEquals(under, _focused) && _mode == GrabMode.None)
        {
            Focus(under);
            if (_settings.AutoRaise)
            {
                _autoRaiseCandidate = under;
                _autoRaiseAt = Environment.TickCount64 + _settings.AutoRaiseDelay;
            }
        }
        else if (hit is null && FocusPolicy.UnfocusOnLeaveToDesktop(_settings.FocusMode) && _focused is not null && _mode == GrabMode.None)
        {
            Focus(null);
        }
    }

    private void UpdateHover(double x, double y)
    {
        var hit = _host.Scene.NodeAt(x, y);
        if (_openMenu is not null && hit is { Node: { } menuNode } && _openMenu.OwnsMenuNode(menuNode))
        {
            LeaveFrameHover(null);
            SetCursor("left_ptr", overClient: false);
            return;
        }

        if (hit?.Surface is not null)
        {
            LeaveFrameHover(null);
            SetCursor(_cursorName, overClient: true);
            return;
        }

        if (hit is { Node: { } node })
        {
            if (_uiIndex.SurfaceOf(node) is { } ui)
            {
                LeaveFrameHover(null);
                SetCursor(_router.CursorAt(x, y) ?? "left_ptr", overClient: false);
                _ = ui;
                return;
            }

            if (FindFrame(node) is { } frameHover)
            {
                LeaveFrameHover(frameHover.Frame);
                _frameHover = frameHover;
                var localX = x - frameHover.Owner.X;
                var localY = y - frameHover.Owner.Y;
                frameHover.Frame.PointerMotion(localX, localY);
                SetCursor(frameHover.Frame.CursorAt(localX, localY) ?? "left_ptr", overClient: false);
                return;
            }
        }

        LeaveFrameHover(null);
        SetCursor("left_ptr", overClient: false);
    }

    private void SetCursor(string name, bool overClient)
    {
        if (overClient)
        {
            if (!_overClient)
            {
                _overClient = true;
                ClientCursorChanged?.Invoke(ShellCursor.Default);
            }

            return;
        }

        if (_overClient || name != _cursorName)
        {
            _overClient = false;
            _cursorName = name;
            CursorChanged?.Invoke(name);
        }
    }

    private (Frame Frame, ManagedWindow Owner)? FindFrame(SceneNode node)
    {
        foreach (var window in _windows)
        {
            if (window.Frame is { } frame && frame.OwnsNode(node))
            {
                return (frame, window);
            }
        }

        return null;
    }

    private void LeaveFrameHover(Frame? except)
    {
        if (_frameHover is { } hover && !ReferenceEquals(hover.Frame, except))
        {
            hover.Frame.PointerLeave();
            _frameHover = null;
        }
    }

    private void OnButton(uint time, uint button, bool pressed)
    {
        if (_mode != GrabMode.None)
        {
            if (!pressed || _mode is GrabMode.KeyboardMove or GrabMode.KeyboardResize)
            {
                EndGrab();
                if (!pressed)
                {
                    _host.Seat.Pointer.NotifyButton(time, button, pressed: false);
                }

                UpdateHover(_cursorX, _cursorY);
                RouteMotion(time, _cursorX, _cursorY);
                return;
            }

            return;
        }

        if (!pressed && _popupDismissButton == button)
        {
            _popupDismissButton = null;
            return;
        }

        if (pressed && _uiPopups.Count > 0 && !WithinOpenPopups(_router.Hovered))
        {
            _popupDismissButton = button;
            PopupDismissRequested?.Invoke();
            return;
        }

        if (_openMenu is { } menu)
        {
            var menuHit = _host.Scene.NodeAt(_cursorX, _cursorY);
            if (menuHit is { Node: { } menuNode } && menu.OwnsMenuNode(menuNode))
            {
                if (button == BtnLeft)
                {
                    menu.MenuPointerButton(menuHit.Value.X, menuHit.Value.Y, pressed);
                    if (!menu.IsMenuOpen)
                    {
                        _openMenu = null;
                        _menuOwner = null;
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

        if (button == BtnLeft && !pressed && _framePress is { } held)
        {
            _framePress = null;
            PrepareMenu(held.Owner, held.Frame);
            held.Frame.PointerButton(_cursorX - held.Owner.X, _cursorY - held.Owner.Y, pressed: false, time);
            if (held.Frame.IsMenuOpen)
            {
                _openMenu = held.Frame;
                _menuOwner = held.Owner;
            }

            return;
        }

        if (_router.Hovered is { } uiSurface)
        {
            if (pressed && ChromeWindowOf(uiSurface) is { } chrome)
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

            _router.PointerButton(time, button, pressed, uiSurface);
            return;
        }

        var nodeHit = _host.Scene.NodeAt(_cursorX, _cursorY);
        if (pressed && nodeHit is { Node: { } frameNode } && nodeHit.Value.Surface is null && FindFrame(frameNode) is { } frameHit)
        {
            OnFramePress(time, button, frameHit);
            return;
        }

        if (pressed && nodeHit?.Surface is { } surface && WindowOf(surface) is { } window)
        {
            if (!window.Active)
            {
                Focus(window);
            }

            if (_settings.RaiseOnClick)
            {
                Raise(window);
            }

            if (ModifierHeld())
            {
                if (button == BtnLeft)
                {
                    BeginMove(window, null, keepTitleOnScreen: false);
                    return;
                }

                if (button == BtnRight && _settings.ResizeWithRightButton)
                {
                    BeginResize(window, EdgesNearest(window), null, keepTitleOnScreen: false);
                    return;
                }

                if (button == BtnMiddle)
                {
                    Lower(window);
                    return;
                }
            }
        }

        _host.Seat.Pointer.NotifyButton(time, button, pressed);
    }

    private ManagedWindow? ChromeWindowOf(IUISurface surface)
    {
        foreach (var window in _windows)
        {
            if (ReferenceEquals(window.Content.UISurface, surface))
            {
                return window;
            }
        }

        return null;
    }

    private ResizeEdges EdgesNearest(ManagedWindow window)
    {
        var box = window.ClientBox;
        var edges = ResizeEdges.None;
        edges |= _cursorX < box.X + (box.Width / 2.0) ? ResizeEdges.Left : ResizeEdges.Right;
        edges |= _cursorY < box.Y + (box.Height / 2.0) ? ResizeEdges.Top : ResizeEdges.Bottom;
        return edges;
    }

    private bool ModifierHeld() => _settings.MouseButtonModifier switch
    {
        "Super" => (_held & ShellModifiers.Super) != 0,
        "Ctrl" => (_held & ShellModifiers.Ctrl) != 0,
        _ => (_held & ShellModifiers.Alt) != 0,
    };

    private void OnFramePress(uint time, uint button, (Frame Frame, ManagedWindow Owner) hit)
    {
        var localX = _cursorX - hit.Owner.X;
        var localY = _cursorY - hit.Owner.Y;
        var part = hit.Frame.PartAt(localX, localY);
        if (part == FramePart.None)
        {
            _host.Seat.Pointer.NotifyButton(time, button, pressed: true);
            return;
        }

        Focus(hit.Owner);
        if (_settings.RaiseOnClick)
        {
            Raise(hit.Owner);
        }

        if (button == BtnLeft)
        {
            if (part == FramePart.Title && IsTitleDoubleClick(hit.Owner))
            {
                RunTitlebarAction(hit.Owner, _settings.DoubleClickTitlebar, localX, localY);
                return;
            }

            _framePress = hit;
            PrepareMenu(hit.Owner, hit.Frame);
            hit.Frame.PointerButton(localX, localY, pressed: true, time);
            return;
        }

        if (part is not (FramePart.Title or FramePart.Icon or FramePart.Border))
        {
            return;
        }

        if (button == BtnMiddle)
        {
            RunTitlebarAction(hit.Owner, _settings.MiddleClickTitlebar, localX, localY);
        }
        else if (button == BtnRight)
        {
            RunTitlebarAction(hit.Owner, _settings.RightClickTitlebar, localX, localY);
        }
    }

    private bool IsTitleDoubleClick(ManagedWindow window)
    {
        var now = Environment.TickCount64;
        var doubleClick = ReferenceEquals(_lastTitleWindow, window) && now - _lastTitlePress <= 400;
        _lastTitleWindow = doubleClick ? null : window;
        _lastTitlePress = doubleClick ? 0 : now;
        return doubleClick;
    }

    private void RunTitlebarAction(ManagedWindow window, TitlebarAction action, double localX, double localY)
    {
        switch (action)
        {
            case TitlebarAction.ToggleMaximize:
                SetMaximized(window, !window.Maximized);
                break;
            case TitlebarAction.ToggleMaximizeHorizontally:
            case TitlebarAction.ToggleMaximizeVertically:
                SetMaximized(window, !window.Maximized);
                break;
            case TitlebarAction.ToggleShade:
                SetShaded(window, !window.Shaded);
                break;
            case TitlebarAction.Minimize:
                SetMinimized(window, true);
                break;
            case TitlebarAction.Lower:
                Lower(window);
                break;
            case TitlebarAction.Menu:
                if (window.Frame is { } frame)
                {
                    PrepareMenu(window, frame);
                    frame.OpenMenu(localX, localY);
                    _openMenu = frame.IsMenuOpen ? frame : null;
                    _menuOwner = frame.IsMenuOpen ? window : null;
                }

                break;
        }
    }

    private void PrepareMenu(ManagedWindow owner, Frame frame)
    {
        frame.MenuOrigin = new Point(owner.X, owner.Y);
        frame.MenuConstraint = Output;
    }

    private void DismissOpenMenu()
    {
        _openMenu?.DismissMenu();
        _openMenu = null;
        _menuOwner = null;
        _menuHovering = false;
    }

    private void OnAxis(uint time, double dx, double dy)
    {
        if (_router.Hovered is { } ui)
        {
            _router.PointerAxis(time, -dx * 10, -dy * 10, ui);
            return;
        }

        if (dy != 0)
        {
            _host.Seat.Pointer.NotifyAxis(time, new Basin.PointerAxis(
                Wayland.WlPointer.Axis.VerticalScroll, -dy * 10, (int)(-dy * 120)));
        }

        if (dx != 0)
        {
            _host.Seat.Pointer.NotifyAxis(time, new Basin.PointerAxis(
                Wayland.WlPointer.Axis.HorizontalScroll, -dx * 10, (int)(-dx * 120)));
        }
    }

    public void BeginMove(ManagedWindow window, uint? serial, bool keepTitleOnScreen = true)
    {
        if (!window.IsMapped || window.Fullscreen)
        {
            return;
        }

        EndGrab();
        var adopted = TryAdoptTouch(serial, out var touchSlot);
        _host.Seat.Pointer.NotifyClearFocus();
        LeaveFrameHover(null);
        _mode = GrabMode.Move;
        _grabWindow = window;
        _grabStart = window.FrameBox;
        _grabKeepsTitle = keepTitleOnScreen;
        _grabX = _cursorX - _grabStart.X;
        _grabY = _cursorY - _grabStart.Y;
        SetCursor("fleur", overClient: false);
        if (adopted)
        {
            BeginTouchGesture(ref _touches[touchSlot]);
        }
    }

    private void DragFrameTo(ManagedWindow window, double x, double y)
    {
        var frame = window.FrameBox with { X = (int)(x - _grabX), Y = (int)(y - _grabY) };
        if (_grabKeepsTitle)
        {
            frame = Constraints.KeepTitleOnScreen(frame, WorkArea, window.TitleHeight);
        }

        window.MoveFrameTo(frame.X, frame.Y);
    }

    private Box ClipResizeToWorkArea(ManagedWindow window, Box client)
    {
        var insets = window.Insets;
        var frame = new Box(
            client.X - insets.Left,
            client.Y - insets.Top,
            client.Width + insets.Left + insets.Right,
            client.Height + insets.Top + insets.Bottom);
        var clipped = Constraints.ClipResize(
            frame, _grabEdges, WorkArea,
            Math.Max(32, window.Content.MinWidth) + insets.Left + insets.Right,
            Math.Max(32, window.Content.MinHeight) + insets.Top + insets.Bottom);
        return new Box(
            clipped.X + insets.Left,
            clipped.Y + insets.Top,
            clipped.Width - insets.Left - insets.Right,
            clipped.Height - insets.Top - insets.Bottom);
    }

    public void BeginResize(ManagedWindow window, ResizeEdges edges, uint? serial, bool keepTitleOnScreen = true)
    {
        if (!window.IsMapped || window.Fullscreen || window.Maximized || edges == ResizeEdges.None)
        {
            return;
        }

        EndGrab();
        var adopted = TryAdoptTouch(serial, out var touchSlot);
        _host.Seat.Pointer.NotifyClearFocus();
        LeaveFrameHover(null);
        _mode = GrabMode.Resize;
        _grabWindow = window;
        _grabEdges = edges;
        _grabKeepsTitle = keepTitleOnScreen;
        _grabX = _cursorX;
        _grabY = _cursorY;
        _grabStart = window.ClientBox;
        window.SetResizing(true);
        SetCursor(ResizeRing.CursorFor(edges), overClient: false);
        if (adopted)
        {
            BeginTouchGesture(ref _touches[touchSlot]);
        }
    }

    private void BeginKeyboardMove(ManagedWindow window)
    {
        if (!window.IsMapped || window.Fullscreen)
        {
            return;
        }

        EndGrab();
        _mode = GrabMode.KeyboardMove;
        _grabWindow = window;
        _grabStart = window.FrameBox;
    }

    private void BeginKeyboardResize(ManagedWindow window)
    {
        if (!window.IsMapped || window.Fullscreen || window.Maximized)
        {
            return;
        }

        EndGrab();
        _mode = GrabMode.KeyboardResize;
        _grabWindow = window;
        _grabStart = window.ClientBox;
    }

    private void EndGrab()
    {
        if (_mode == GrabMode.None)
        {
            return;
        }

        var window = _grabWindow;
        var mode = _mode;
        _mode = GrabMode.None;
        _grabWindow = null;
        _framePress = null;
        EndTouchGesture();
        if (window is null || !window.IsMapped)
        {
            return;
        }

        if (mode == GrabMode.Resize)
        {
            window.SetResizing(false);
        }

        if (mode is GrabMode.Move or GrabMode.KeyboardMove)
        {
            var edge = mode == GrabMode.Move
                ? Tiling.EdgeAt(new Point((int)_cursorX, (int)_cursorY), Output, WorkArea, _settings.Tiling, _settings.TopTiling)
                : TileEdge.None;
            if (edge != TileEdge.None)
            {
                SetTile(window, edge);
            }
            else if (!window.Maximized && window.Tile == TileEdge.None)
            {
                var frame = Constraints.KeepTitleOnScreen(window.FrameBox, WorkArea, window.TitleHeight);
                if (frame.X != window.FrameBox.X || frame.Y != window.FrameBox.Y)
                {
                    window.MoveFrameTo(frame.X, frame.Y);
                }
            }
        }

        Publish();
    }

    private void CancelGrab()
    {
        if (_mode == GrabMode.None || _grabWindow is not { } window)
        {
            return;
        }

        var mode = _mode;
        _mode = GrabMode.None;
        _grabWindow = null;
        EndTouchGesture();
        if (mode is GrabMode.Move or GrabMode.KeyboardMove)
        {
            window.MoveFrameTo(_grabStart.X, _grabStart.Y);
        }
        else
        {
            window.SetResizing(false);
            window.ResizeClientTo(_grabStart, ResizeEdges.None);
        }
    }

    private void HandleKey(uint time, uint code, bool pressed)
    {
        TrackModifier(code, pressed);
        if (pressed)
        {
            _pressedKeys.Add(code);
        }
        else
        {
            _pressedKeys.Remove(code);
        }

        if (_switcherOpen)
        {
            if (!pressed && ShellKeyCodes.ModifierOf(code) != ShellModifiers.None && (_held & ShellModifiers.Alt) == 0)
            {
                EndSwitcher(commit: true);
                return;
            }

            if (pressed && code == 1)
            {
                EndSwitcher(commit: false);
                return;
            }

            if (pressed && _keys.Match(_held, code) is { } switcherKey)
            {
                RunKey(switcherKey);
            }

            return;
        }

        if (_mode is GrabMode.KeyboardMove or GrabMode.KeyboardResize)
        {
            if (pressed)
            {
                HandleKeyboardGrab(code);
            }

            return;
        }

        if (pressed && code == 1 && _openMenu is not null)
        {
            DismissOpenMenu();
            return;
        }

        if (pressed && _keys.Match(_held, code) is { } name)
        {
            _host.Seat.Keyboard.NotifyKeyConsumed(code, pressed);
            RunKey(name);
            return;
        }

        if (_router.KeyboardFocus is not null)
        {
            if (pressed && code == 1 && _panelHasKeyboard)
            {
                ReleasePanelKeyboard();
            }

            _router.Key(time, code, pressed);
            return;
        }

        _host.Seat.Keyboard.NotifyKey(time, code, pressed);
    }

    private void TrackModifier(uint code, bool pressed)
    {
        var modifier = ShellKeyCodes.ModifierOf(code);
        if (modifier == ShellModifiers.None)
        {
            return;
        }

        if (pressed)
        {
            _held |= modifier;
        }
        else
        {
            _held &= ~modifier;
        }
    }

    private void ReleaseHeldKeys()
    {
        _held = ShellModifiers.None;
        _pressedKeys.Clear();
    }

    private void HandleKeyboardGrab(uint code)
    {
        if (_grabWindow is not { } window)
        {
            _mode = GrabMode.None;
            return;
        }

        const int step = 10;
        var dx = code switch { 105 => -step, 106 => step, _ => 0 };
        var dy = code switch { 103 => -step, 108 => step, _ => 0 };
        switch (code)
        {
            case 1:
                CancelGrab();
                return;
            case 28 or 96 or 57:
                EndGrab();
                return;
        }

        if (dx == 0 && dy == 0)
        {
            return;
        }

        if (_mode == GrabMode.KeyboardMove)
        {
            var frame = window.FrameBox;
            window.MoveFrameTo(frame.X + dx, frame.Y + dy);
        }
        else
        {
            var client = window.ClientBox;
            var (width, height) = Constraints.ClampSize(
                client.Width + dx, client.Height + dy, window.Content.MinWidth, window.Content.MinHeight,
                window.Content.MaxWidth, window.Content.MaxHeight);
            window.ResizeClientTo(client with { Width = Math.Max(32, width), Height = Math.Max(32, height) }, ResizeEdges.Bottom | ResizeEdges.Right);
        }
    }

    private void RunKey(string name)
    {
        var focused = _focused;
        switch (name)
        {
            case "switch-windows":
            case "switch-windows-all":
                AdvanceSwitcher(sameGroup: false, backward: false);
                break;
            case "switch-windows-backward":
            case "switch-windows-all-backward":
                AdvanceSwitcher(sameGroup: false, backward: true);
                break;
            case "switch-group":
                AdvanceSwitcher(sameGroup: true, backward: false);
                break;
            case "switch-group-backward":
                AdvanceSwitcher(sameGroup: true, backward: true);
                break;
            case "cycle-windows":
            case "cycle-group":
                CycleFocus(sameGroup: name == "cycle-group", backward: false);
                break;
            case "cycle-windows-backward":
            case "cycle-group-backward":
                CycleFocus(sameGroup: name == "cycle-group-backward", backward: true);
                break;
            case "switch-to-workspace-left":
                SwitchNeighbor(WorkspaceDirection.Left);
                break;
            case "switch-to-workspace-right":
                SwitchNeighbor(WorkspaceDirection.Right);
                break;
            case "switch-to-workspace-up":
                SwitchNeighbor(WorkspaceDirection.Up);
                break;
            case "switch-to-workspace-down":
                SwitchNeighbor(WorkspaceDirection.Down);
                break;
            case "switch-to-workspace-prev":
                SwitchWorkspace(_previousWorkspace);
                break;
            case "move-to-workspace-left":
                MoveToNeighbor(WorkspaceDirection.Left);
                break;
            case "move-to-workspace-right":
                MoveToNeighbor(WorkspaceDirection.Right);
                break;
            case "move-to-workspace-up":
                MoveToNeighbor(WorkspaceDirection.Up);
                break;
            case "move-to-workspace-down":
                MoveToNeighbor(WorkspaceDirection.Down);
                break;
            case "show-desktop":
                ToggleShowDesktop();
                break;
            case "panel-main-menu":
                MainMenuRequested?.Invoke();
                break;
            case "toggle-host-fullscreen":
                HostFullScreenRequested?.Invoke();
                break;
            case "activate-window-menu":
                if (focused is not null)
                {
                    ShowWindowMenu(focused, focused.FrameBox.X, focused.ClientBox.Y);
                }

                break;
            case "close":
                if (focused is not null)
                {
                    CloseWindow(focused);
                }

                break;
            case "minimize":
                if (focused is not null)
                {
                    SetMinimized(focused, true);
                }

                break;
            case "toggle-fullscreen":
                if (focused is not null)
                {
                    SetFullscreen(focused, !focused.Fullscreen);
                }

                break;
            case "toggle-maximized":
                if (focused is not null)
                {
                    SetMaximized(focused, !focused.Maximized);
                }

                break;
            case "maximize":
            case "maximize-vertically":
            case "maximize-horizontally":
                if (focused is not null)
                {
                    SetMaximized(focused, true);
                }

                break;
            case "unmaximize":
                if (focused is not null)
                {
                    SetMaximized(focused, false);
                }

                break;
            case "toggle-above":
                if (focused is not null)
                {
                    SetAbove(focused, !focused.Above);
                }

                break;
            case "toggle-shaded":
                if (focused is not null)
                {
                    SetShaded(focused, !focused.Shaded);
                }

                break;
            case "toggle-on-all-workspaces":
                if (focused is not null)
                {
                    SetSticky(focused, !focused.Sticky);
                }

                break;
            case "begin-move":
                if (focused is not null)
                {
                    BeginKeyboardMove(focused);
                }

                break;
            case "begin-resize":
                if (focused is not null)
                {
                    BeginKeyboardResize(focused);
                }

                break;
            case "raise":
                if (focused is not null)
                {
                    Raise(focused);
                }

                break;
            case "lower":
                if (focused is not null)
                {
                    Lower(focused);
                }

                break;
            case "raise-or-lower":
                if (focused is not null)
                {
                    Raise(focused);
                }

                break;
            case "tile-to-side-e":
            case "tile-to-corner-ne":
            case "tile-to-corner-se":
                if (focused is not null)
                {
                    SetTile(focused, TileEdge.Right);
                }

                break;
            case "tile-to-side-w":
            case "tile-to-corner-nw":
            case "tile-to-corner-sw":
                if (focused is not null)
                {
                    SetTile(focused, TileEdge.Left);
                }

                break;
            case "move-to-center":
                if (focused is not null)
                {
                    MoveToAnchor(focused, 0.5, 0.5);
                }

                break;
            case "move-to-corner-nw":
                MoveFocusedToAnchor(0, 0);
                break;
            case "move-to-corner-ne":
                MoveFocusedToAnchor(1, 0);
                break;
            case "move-to-corner-sw":
                MoveFocusedToAnchor(0, 1);
                break;
            case "move-to-corner-se":
                MoveFocusedToAnchor(1, 1);
                break;
            case "move-to-side-n":
                MoveFocusedToAnchor(null, 0);
                break;
            case "move-to-side-s":
                MoveFocusedToAnchor(null, 1);
                break;
            case "move-to-side-e":
                MoveFocusedToAnchor(1, null);
                break;
            case "move-to-side-w":
                MoveFocusedToAnchor(0, null);
                break;
            default:
                if (name.StartsWith("switch-to-workspace-", StringComparison.Ordinal)
                    && int.TryParse(name["switch-to-workspace-".Length..], out var switchTo))
                {
                    SwitchWorkspace(switchTo - 1);
                }
                else if (name.StartsWith("move-to-workspace-", StringComparison.Ordinal)
                    && int.TryParse(name["move-to-workspace-".Length..], out var moveTo) && focused is not null)
                {
                    MoveToWorkspace(focused, moveTo - 1);
                }

                break;
        }
    }

    private int _previousWorkspace;

    private void SwitchNeighbor(WorkspaceDirection direction)
    {
        if (_workspaces.Neighbor(direction) is { } target)
        {
            _previousWorkspace = _workspaces.Current;
            SwitchWorkspace(target);
        }
    }

    private void MoveToNeighbor(WorkspaceDirection direction)
    {
        if (_focused is { } focused && _workspaces.Neighbor(direction) is { } target)
        {
            MoveToWorkspace(focused, target);
            _previousWorkspace = _workspaces.Current;
            SwitchWorkspace(target);
            Focus(focused);
        }
    }

    private void MoveFocusedToAnchor(double? fx, double? fy)
    {
        if (_focused is { } focused)
        {
            MoveToAnchor(focused, fx, fy);
        }
    }

    private void MoveToAnchor(ManagedWindow window, double? fx, double? fy)
    {
        if (window.Maximized || window.Fullscreen)
        {
            return;
        }

        var frame = window.FrameBox;
        var x = fx is { } px ? WorkArea.X + (int)((WorkArea.Width - frame.Width) * px) : frame.X;
        var y = fy is { } py ? WorkArea.Y + (int)((WorkArea.Height - frame.Height) * py) : frame.Y;
        window.MoveFrameTo(x, y);
        Publish();
    }

    private void CycleFocus(bool sameGroup, bool backward)
    {
        var order = SwitchOrder(sameGroup);
        if (order.Count < 2)
        {
            return;
        }

        var target = backward ? order[^1] : order[1];
        ActivateWindow(target);
    }

    private void AdvanceSwitcher(bool sameGroup, bool backward)
    {
        if (!_switcherOpen)
        {
            var order = SwitchOrder(sameGroup);
            if (order.Count < 2)
            {
                return;
            }

            _switcherOpen = true;
            _switcherOrder = order;
            _switcherIndex = backward ? order.Count - 1 : 1;
            SwitcherShown?.Invoke(order, _switcherIndex);
            return;
        }

        _switcherIndex = backward
            ? (_switcherIndex - 1 + _switcherOrder.Count) % _switcherOrder.Count
            : (_switcherIndex + 1) % _switcherOrder.Count;
        SwitcherMoved?.Invoke(_switcherIndex);
    }

    private void EndSwitcher(bool commit)
    {
        if (!_switcherOpen)
        {
            return;
        }

        _switcherOpen = false;
        var chosen = commit && _switcherIndex >= 0 && _switcherIndex < _switcherOrder.Count ? _switcherOrder[_switcherIndex] : null;
        _switcherOrder = [];
        SwitcherHidden?.Invoke();
        if (chosen is not null && chosen.IsMapped)
        {
            ActivateWindow(chosen);
        }
    }

    public bool SwitcherOpen => _switcherOpen;

    public void GivePanelKeyboard(IUISurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _panelHasKeyboard = true;
        _host.Seat.Keyboard.NotifyClearFocus();
        _router.SetKeyboardFocus(surface, [.. _pressedKeys]);
    }

    public void ReleasePanelKeyboard()
    {
        if (!_panelHasKeyboard)
        {
            return;
        }

        _panelHasKeyboard = false;
        ApplyKeyboardFocus();
    }

    public bool PanelHasKeyboard => _panelHasKeyboard;

    public event Action? PopupDismissRequested;

    public bool HasOpenPopups => _uiPopups.Count > 0;

    public void PopupOpened(IUISurface popup, IUISurface owner)
    {
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(owner);
        _uiPopups[popup] = owner;
    }

    public void PopupClosed(IUISurface popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        _uiPopups.Remove(popup);
    }

    private bool WithinOpenPopups(IUISurface? surface) =>
        surface is not null && (_uiPopups.ContainsKey(surface) || _uiPopups.ContainsValue(surface));

    private void OnShapeRequested(CursorShape shape)
    {
        if (_overClient)
        {
            ClientCursorChanged?.Invoke(ShellCursor.Of(shape));
        }
    }

    private void OnCursorRequested(Basin.Seat.CursorRequest request)
    {
        if (_cursorSurface is { } previous && _cursorCommitted is { } committed)
        {
            previous.Committed -= committed;
        }

        _cursorSurface = null;
        _cursorCommitted = null;
        if (request.Surface is not { } cursorSurface)
        {
            if (_overClient)
            {
                ClientCursorChanged?.Invoke(ShellCursor.None);
            }

            return;
        }

        var hotspotX = request.HotspotX;
        var hotspotY = request.HotspotY;
        _cursorSurface = cursorSurface;
        _cursorCommitted = () => ApplyCursorSurface(cursorSurface, hotspotX, hotspotY);
        cursorSurface.Committed += _cursorCommitted;
        ApplyCursorSurface(cursorSurface, hotspotX, hotspotY);
    }

    private void ApplyCursorSurface(Surface surface, int hotspotX, int hotspotY)
    {
        if (_overClient)
        {
            ClientCursorChanged?.Invoke(ShellCursor.Image(surface, hotspotX, hotspotY));
        }
    }
}
