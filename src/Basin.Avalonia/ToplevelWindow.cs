using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;
using static Basin.Avalonia.AvaloniaLog;

namespace Basin.Avalonia;

public sealed class ToplevelWindow : Window
{
    private readonly ToplevelWindows _manager;
    private readonly int _id;
    private readonly BasinToplevelView _view;
    private bool _applyingClientState;
    private bool _closing;

    internal ToplevelWindow(
        ToplevelWindows manager,
        int id,
        ToplevelInfo info,
        bool serverSideDecorations,
        (int Width, int Height) minimum,
        (int Width, int Height) maximum)
    {
        _manager = manager;
        _id = id;
        _view = new BasinToplevelView(manager.Host, _ => manager.CreateView(id));
        var background = new global::Avalonia.Controls.Panel
        {
            Background = global::Avalonia.Media.Brushes.Transparent,
        };
        background.Children.Add(_view);
        Content = background;
        _clientTitle = info.Title.Length > 0 ? info.Title : info.AppId.Length > 0 ? info.AppId : "Wayland";
        Title = _clientTitle;
        Width = info.Width;
        Height = info.Height;
        SizeToContent = SizeToContent.Manual;
        Background = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(
            global::Avalonia.Media.Color.FromRgb(0, 0, 0));
        ApplyDecorations(serverSideDecorations);
        if (minimum.Width > 0)
        {
            MinWidth = minimum.Width;
        }

        if (minimum.Height > 0)
        {
            MinHeight = minimum.Height;
        }

        if (maximum.Width > 0)
        {
            MaxWidth = maximum.Width;
        }

        if (maximum.Height > 0)
        {
            MaxHeight = maximum.Height;
        }

        PropertyChanged += OnWindowPropertyChanged;
        Activated += (_, _) =>
        {
            _view.Focus();
            _manager.HostActivated(_id, true);
            _manager.NotifyActivatedUi();
        };
        Deactivated += (_, _) =>
        {
            EndGesture();
            ReleasePressedKeys();
            _manager.HostActivated(_id, false);
        };
        TextInputMethodClientRequested += (_, e) =>
        {
            if (_manager.TextInput is { } textInput && textInput.IsActiveOn(this))
            {
                e.Client = textInput.Client;
            }
        };
        TextInput += (_, e) =>
        {
            if (_manager.TextInput is { } textInput && textInput.IsActiveOn(this) && !string.IsNullOrEmpty(e.Text))
            {
                textInput.CommitFromHost(e.Text!);
                e.Handled = true;
            }
        };
        global::Avalonia.Input.DragDrop.SetAllowDrop(this, true);
        AddHandler(global::Avalonia.Input.DragDrop.DragEnterEvent, OnHostDragEnter);
        AddHandler(global::Avalonia.Input.DragDrop.DragOverEvent, OnHostDragOver);
        AddHandler(global::Avalonia.Input.DragDrop.DropEvent, OnHostDrop);
        AddHandler(global::Avalonia.Input.DragDrop.DragLeaveEvent, OnHostDragLeave);
        PointerEntered += OnHostPointerEntered;
        PointerMoved += OnHostPointerMoved;
        PointerExited += OnHostPointerExited;
        PointerPressed += OnHostPointerPressed;
        PointerReleased += OnHostPointerReleased;
        PointerWheelChanged += OnHostPointerWheel;
        PointerCaptureLost += OnHostPointerCaptureLost;
        AddHandler(
            global::Avalonia.Input.InputElement.KeyDownEvent,
            OnHostKeyDown,
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(
            global::Avalonia.Input.InputElement.KeyUpEvent,
            OnHostKeyUp,
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PositionChanged += (_, _) => TrackScreen();
        Opened += (_, _) =>
        {
            ApplyHostResizable();
            TrackScreen();
            _manager.HostScaleChanged(_id, RenderScaling, ScaleIsAuthoritative);
            foreach (var delay in (int[])[100, 500, 1500])
            {
                DispatcherTimer.RunOnce(() =>
                {
                    TrackScreen();
                    _manager.HostScaleChanged(_id, RenderScaling, ScaleIsAuthoritative);
                }, TimeSpan.FromMilliseconds(delay));
            }
        };
        ScalingChanged += (_, _) =>
        {
            _scaleSettled = true;
            TrackScreen();
            _manager.HostScaleChanged(_id, RenderScaling, authoritative: true);
        };
        Closing += (_, e) =>
        {
            if (!_closing)
            {
                e.Cancel = true;
                _manager.HostCloseRequested(_id);
            }
        };
    }

    internal BasinToplevelView View => _view;

    internal void RequeryIme() => _view.RaiseEvent(new global::Avalonia.Input.TextInput.TextInputMethodClientRequeryRequestedEventArgs
    {
        RoutedEvent = global::Avalonia.Input.InputMethod.TextInputMethodClientRequeryRequestedEvent,
    });

    internal int Id => _id;

    private readonly HashSet<uint> _pressedKeys = [];

    private readonly HashSet<uint> _pressedButtons = [];

    internal void ApplyCursor(global::Avalonia.Input.Cursor cursor)
    {
        if (!_edgeCursor)
        {
            Cursor = cursor;
        }
    }

    private static uint Now => (uint)Environment.TickCount;

    private void Send(InputKind kind, global::Avalonia.Input.PointerEventArgs? pointer = null, uint code = 0, bool pressed = false, double dx = 0, double dy = 0)
    {
        var position = pointer?.GetPosition(_view) ?? default;
        _manager.Enqueue(new BasinInputEvent
        {
            Kind = kind,
            WindowId = _id,
            TimeMs = Now,
            X = position.X,
            Y = position.Y,
            Code = code,
            Pressed = pressed,
            DeltaX = dx,
            DeltaY = dy,
            TouchId = pointer?.Pointer.Id ?? 0,
        });
    }

    private void OnHostPointerEntered(object? sender, global::Avalonia.Input.PointerEventArgs e)
    {
        if (_manualEdge is not null)
        {
            return;
        }

        EndGesture();
        if (e.Pointer.Type != global::Avalonia.Input.PointerType.Touch)
        {
            Send(InputKind.PointerEnter, e);
        }
    }

    private void OnHostPointerMoved(object? sender, global::Avalonia.Input.PointerEventArgs e)
    {
        if (_resizing)
        {
            if (_manualEdge is { } manualEdge)
            {
                ContinueResizeDrag(manualEdge, e);
            }

            return;
        }

        if (_manager.HasDragIcon)
        {
            var position = e.GetPosition(_view);
            _manager.MoveDragIcon(_view.PointToScreen(new global::Avalonia.Point(position.X + 4, position.Y + 4)));
        }

        if (e.Pointer.Type != global::Avalonia.Input.PointerType.Touch)
        {
            if (EdgeAt(e.GetPosition(this)) is { } edge)
            {
                _edgeCursor = false;
                ApplyCursor(AvaloniaCursor.For(ShapeFor(edge)));
                _edgeCursor = true;
            }
            else if (_edgeCursor)
            {
                _edgeCursor = false;
                ApplyCursor(new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Arrow));
            }
        }

        Send(e.Pointer.Type == global::Avalonia.Input.PointerType.Touch ? InputKind.TouchMotion : InputKind.PointerMotion, e);
    }

    private static List<(string Mime, byte[] Data)> PayloadOf(global::Avalonia.Input.DragEventArgs e)
    {
        var payload = new List<(string, byte[])>();
        if (global::Avalonia.Input.DataTransferExtensions.TryGetText(e.DataTransfer) is { } text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            payload.Add(("text/plain;charset=utf-8", bytes));
            payload.Add(("text/plain", bytes));
        }

        if (global::Avalonia.Input.DataTransferExtensions.TryGetFiles(e.DataTransfer) is { Length: > 0 } files)
        {
            var uris = new System.Text.StringBuilder();
            foreach (var file in files)
            {
                uris.Append(file.Path.AbsoluteUri).Append("\r\n");
            }

            payload.Add(("text/uri-list", System.Text.Encoding.UTF8.GetBytes(uris.ToString())));
        }

        return payload;
    }

    private void OnHostDragEnter(object? sender, global::Avalonia.Input.DragEventArgs e)
    {
        var position = e.GetPosition(_view);
        e.DragEffects = global::Avalonia.Input.DragDropEffects.Copy;
        _manager.HostDragEnter(_id, position.X, position.Y, PayloadOf(e));
        e.Handled = true;
    }

    private void OnHostDragOver(object? sender, global::Avalonia.Input.DragEventArgs e)
    {
        var position = e.GetPosition(_view);
        e.DragEffects = global::Avalonia.Input.DragDropEffects.Copy;
        _manager.HostDragMotion(_id, position.X, position.Y);
        e.Handled = true;
    }

    private void OnHostDrop(object? sender, global::Avalonia.Input.DragEventArgs e)
    {
        var position = e.GetPosition(_view);
        _manager.HostDragMotion(_id, position.X, position.Y);
        _manager.HostDragDrop();
        e.Handled = true;
    }

    private void OnHostDragLeave(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        _manager.HostDragLeave();
    }

    private void OnHostPointerExited(object? sender, global::Avalonia.Input.PointerEventArgs e)
    {
        _edgeCursor = false;
        if (_resizing)
        {
            return;
        }

        if (_lastPress is { } press && _manager.ClientDragHandoffText() is { } dragText)
        {
            HandOffDragToHost(press, dragText);
            return;
        }

        if (e.Pointer.Type != global::Avalonia.Input.PointerType.Touch)
        {
            Send(InputKind.PointerLeave, e);
        }
    }

    private async void HandOffDragToHost(global::Avalonia.Input.PointerPressedEventArgs e, string text)
    {
        var transfer = new global::Avalonia.Input.DataTransfer();
        transfer.Add(global::Avalonia.Input.DataTransferItem.CreateText(text));
        global::Avalonia.Input.DragDropEffects result;
        try
        {
            result = await global::Avalonia.Input.DragDrop.DoDragDropAsync(
                e, transfer, global::Avalonia.Input.DragDropEffects.Copy | global::Avalonia.Input.DragDropEffects.Move);
        }
        catch (global::System.PlatformNotSupportedException)
        {
            result = global::Avalonia.Input.DragDropEffects.None;
        }

        _manager.FinishClientDrag(result != global::Avalonia.Input.DragDropEffects.None);
    }

    private global::Avalonia.Input.PointerPressedEventArgs? _lastPress;
    private global::Avalonia.Input.IPointer? _gesturePointer;
    private bool _resizing;
    private (int Left, int Top, int Right, int Bottom) _resizeInsets;
    private bool _edgeCursor;
    private WindowEdge? _manualEdge;
    private PixelPoint _manualPointerStart;
    private Size _manualSizeStart;
    private PixelPoint _manualPositionStart;

    internal void ApplyResizeInsets((int Left, int Top, int Right, int Bottom) insets) => _resizeInsets = insets;

    private WindowEdge? EdgeAt(global::Avalonia.Point position)
    {
        if (WindowState != WindowState.Normal || !CanResize)
        {
            return null;
        }

        var (insetLeft, insetTop, insetRight, insetBottom) = _resizeInsets;
        if (insetLeft == 0 && insetTop == 0 && insetRight == 0 && insetBottom == 0)
        {
            return null;
        }

        var size = ClientSize;
        var left = position.X < insetLeft;
        var right = position.X >= size.Width - insetRight;
        var top = position.Y < insetTop;
        var bottom = position.Y >= size.Height - insetBottom;
        if (top)
        {
            return left ? WindowEdge.NorthWest : right ? WindowEdge.NorthEast : WindowEdge.North;
        }

        if (bottom)
        {
            return left ? WindowEdge.SouthWest : right ? WindowEdge.SouthEast : WindowEdge.South;
        }

        return left ? WindowEdge.West : right ? WindowEdge.East : null;
    }

    private static Capabilities.CursorShape ShapeFor(WindowEdge edge) => edge switch
    {
        WindowEdge.North => Capabilities.CursorShape.NResize,
        WindowEdge.South => Capabilities.CursorShape.SResize,
        WindowEdge.West => Capabilities.CursorShape.WResize,
        WindowEdge.East => Capabilities.CursorShape.EResize,
        WindowEdge.NorthWest => Capabilities.CursorShape.NwResize,
        WindowEdge.NorthEast => Capabilities.CursorShape.NeResize,
        WindowEdge.SouthWest => Capabilities.CursorShape.SwResize,
        WindowEdge.SouthEast => Capabilities.CursorShape.SeResize,
        _ => Capabilities.CursorShape.Default,
    };

    internal void BeginClientMove()
    {
        if (_resizing || _gesturePointer is null || _lastPress is not { } press)
        {
            return;
        }

        foreach (var button in _pressedButtons)
        {
            Send(InputKind.PointerButton, code: button, pressed: false);
        }

        _pressedButtons.Clear();
        Send(InputKind.PointerLeave);
        _gesturePointer = null;
        BeginMoveDrag(press);
    }

    internal void BeginClientResize(ResizeEdges edges)
    {
        if (_resizing || _gesturePointer is null || _lastPress is not { } press || EdgeFor(edges) is not { } edge)
        {
            return;
        }

        Log.Debug($"client resize: edges={edges}");

        foreach (var button in _pressedButtons)
        {
            Send(InputKind.PointerButton, code: button, pressed: false);
        }

        _pressedButtons.Clear();
        Send(InputKind.PointerLeave);
        _gesturePointer = null;
        _resizing = true;
        _manager.ClientResizeStateChanged(_id, true);
        StartResizeDrag(edge, press);
    }

    private static WindowEdge? EdgeFor(ResizeEdges edges) => edges switch
    {
        ResizeEdges.Top => WindowEdge.North,
        ResizeEdges.Bottom => WindowEdge.South,
        ResizeEdges.Left => WindowEdge.West,
        ResizeEdges.Right => WindowEdge.East,
        ResizeEdges.TopLeft => WindowEdge.NorthWest,
        ResizeEdges.TopRight => WindowEdge.NorthEast,
        ResizeEdges.BottomLeft => WindowEdge.SouthWest,
        ResizeEdges.BottomRight => WindowEdge.SouthEast,
        _ => null,
    };

    private void EndGesture()
    {
        _manualEdge = null;
        if (!_resizing)
        {
            return;
        }

        _manager.ClientResizeStateChanged(_id, false);
        _resizing = false;
    }

    private void StartResizeDrag(WindowEdge edge, global::Avalonia.Input.PointerPressedEventArgs press)
    {
        if (!OperatingSystem.IsMacOS())
        {
            BeginResizeDrag(edge, press);
            return;
        }

        _manualEdge = edge;
        _manualPointerStart = this.PointToScreen(press.GetPosition(this));
        _manualSizeStart = ClientSize;
        _manualPositionStart = Position;
    }

    private void ContinueResizeDrag(WindowEdge edge, global::Avalonia.Input.PointerEventArgs e)
    {
        var pointer = OperatingSystem.IsMacOS() && MacPointerLocation.TryGet() is { } location
            ? location
            : this.PointToScreen(e.GetPosition(this));
        var scale = DesktopScaling > 0 ? DesktopScaling : 1.0;
        var dx = (pointer.X - _manualPointerStart.X) / scale;
        var dy = (pointer.Y - _manualPointerStart.Y) / scale;
        var width = _manualSizeStart.Width;
        var height = _manualSizeStart.Height;
        var position = _manualPositionStart;
        if (edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast)
        {
            width = Math.Clamp(_manualSizeStart.Width + dx, Math.Max(1, MinWidth), MaxWidth);
        }
        else if (edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest)
        {
            width = Math.Clamp(_manualSizeStart.Width - dx, Math.Max(1, MinWidth), MaxWidth);
            position = position.WithX(position.X + (int)Math.Round((_manualSizeStart.Width - width) * scale));
        }

        if (edge is WindowEdge.South or WindowEdge.SouthEast or WindowEdge.SouthWest)
        {
            height = Math.Clamp(_manualSizeStart.Height + dy, Math.Max(1, MinHeight), MaxHeight);
        }
        else if (edge is WindowEdge.North or WindowEdge.NorthEast or WindowEdge.NorthWest)
        {
            height = Math.Clamp(_manualSizeStart.Height - dy, Math.Max(1, MinHeight), MaxHeight);
            position = position.WithY(position.Y + (int)Math.Round((_manualSizeStart.Height - height) * scale));
        }

        Width = width;
        Height = height;
        if (position != _manualPositionStart)
        {
            Position = position;
        }
    }

    private void OnHostPointerPressed(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
    {
        _lastPress = e;
        if (e.Pointer.Type == global::Avalonia.Input.PointerType.Touch)
        {
            Send(InputKind.TouchDown, e);
            return;
        }

        EndGesture();
        _view.Focus();
        _gesturePointer = e.Pointer;
        if (IsActive && e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Alt))
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                BeginClientResize(ResizeEdges.Bottom | ResizeEdges.Right);
            }
            else
            {
                BeginClientMove();
            }

            e.Handled = true;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && EdgeAt(e.GetPosition(this)) is { } edge)
        {
            Log.Debug($"host resize: edge={edge}");
            Send(InputKind.PointerLeave);
            _gesturePointer = null;
            _resizing = true;
            _manager.ClientResizeStateChanged(_id, true);
            StartResizeDrag(edge, e);
            e.Handled = true;
            return;
        }

        Send(InputKind.PointerMotion, e);
        var button = AvaloniaKeyMap.ButtonFor(e.GetCurrentPoint(_view).Properties.PointerUpdateKind);
        if (button != 0 && _pressedButtons.Add(button))
        {
            Send(InputKind.PointerButton, e, button, pressed: true);
        }
    }

    private void OnHostPointerReleased(object? sender, global::Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (e.Pointer.Type == global::Avalonia.Input.PointerType.Touch)
        {
            Send(InputKind.TouchUp, e);
            return;
        }

        EndGesture();
        _gesturePointer = null;
        var button = AvaloniaKeyMap.ButtonFor(e.GetCurrentPoint(_view).Properties.PointerUpdateKind);
        if (button != 0 && _pressedButtons.Remove(button))
        {
            Send(InputKind.PointerButton, e, button, pressed: false);
        }
    }

    private void OnHostPointerWheel(object? sender, global::Avalonia.Input.PointerWheelEventArgs e)
    {
        Send(InputKind.PointerAxis, e, dx: e.Delta.X, dy: e.Delta.Y);
    }

    private void OnHostPointerCaptureLost(object? sender, global::Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (e.Pointer.Type == global::Avalonia.Input.PointerType.Touch)
        {
            Send(InputKind.TouchUp, code: (uint)e.Pointer.Id);
        }
    }

    private void OnHostKeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == global::Avalonia.Input.Key.ImeProcessed)
        {
            return;
        }

        var code = AvaloniaKeyMap.EvdevFor(e.PhysicalKey);
        if (code == 0)
        {
            return;
        }

        if (KeyFilter is { } filter && filter(code, true))
        {
            e.Handled = true;
            return;
        }

        if (_pressedKeys.Add(code))
        {
            Send(InputKind.Key, code: code, pressed: true);
            e.Handled = true;
        }
    }

    public Func<uint, bool, bool>? KeyFilter { get; set; }

    public void InjectKey(uint code, bool pressed)
    {
        if (code == 0)
        {
            return;
        }

        if (pressed ? !_pressedKeys.Add(code) : !_pressedKeys.Remove(code))
        {
            return;
        }

        Send(InputKind.Key, code: code, pressed: pressed);
    }

    private void ReleasePressedKeys()
    {
        foreach (var code in _pressedKeys)
        {
            Send(InputKind.Key, code: code, pressed: false);
        }

        _pressedKeys.Clear();
    }

    private void OnHostKeyUp(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == global::Avalonia.Input.Key.ImeProcessed)
        {
            return;
        }

        var code = AvaloniaKeyMap.EvdevFor(e.PhysicalKey);
        if (code == 0)
        {
            return;
        }

        if (KeyFilter is { } filter && filter(code, false))
        {
            e.Handled = true;
            return;
        }

        if (_pressedKeys.Remove(code))
        {
            Send(InputKind.Key, code: code, pressed: false);
            e.Handled = true;
        }
    }

    private string _clientTitle = "Wayland";
    private string? _titleOverride;

    internal void ApplyTitle(string title)
    {
        _clientTitle = title;
        if (_titleOverride is null)
        {
            Title = title;
        }
    }

    public void OverrideTitle(string? title)
    {
        _titleOverride = title;
        Title = title ?? _clientTitle;
    }

    internal void ApplyClientSize(int width, int height)
    {
        _applyingClientState = true;
        try
        {
            Width = width;
            Height = height;
        }
        finally
        {
            _applyingClientState = false;
        }
    }

    internal void ApplyState(WindowState state)
    {
        WindowState = state;
    }

    internal void ApplyVisible(bool visible)
    {
        if (visible)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    internal void ApplyDecorations(bool serverSide)
    {
        WindowDecorations = serverSide ? WindowDecorations.Full : WindowDecorations.None;
        TransparencyLevelHint = serverSide
            ? []
            : [WindowTransparencyLevel.Transparent];
        Background = serverSide ? Background : null;
        ApplyHostResizable();
    }

    private void ApplyHostResizable()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var resizable = CanResize && (MaxWidth > MinWidth || MaxHeight > MinHeight);
        MacResizable.Apply(TryGetPlatformHandle(), resizable);
    }

    internal async Task CloseFromCompositorAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        await _view.ShutdownAsync();
        Close();
    }

    private string? _screenKey;
    private double _observedScale;
    private bool _scaleSettled;

    private bool ScaleIsAuthoritative => _scaleSettled || RenderScaling != 1.0;

    private void TrackScreen()
    {
        var screens = Screens;
        var screen = screens.ScreenFromTopLevel(this) ?? screens.ScreenFromWindow(this);
        var key = HostScreens.KeyFor(screens, screen);
        if (key is not null && RenderScaling > 0 && ScaleIsAuthoritative
            && (key != _screenKey || RenderScaling != _observedScale))
        {
            _observedScale = RenderScaling;
            _manager.HostScreenScaleObserved(key, RenderScaling);
        }

        if (key != _screenKey)
        {
            _screenKey = key;
            _manager.HostScreenChanged(_id, key);
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ClientSizeProperty)
        {
            TrackScreen();
            if (_applyingClientState)
            {
                return;
            }

            var size = ClientSize;
            Log.Debug($"host resized: client size {size.Width}x{size.Height} state {WindowState}");
            _manager.HostResized(_id, Math.Max(1, (int)Math.Round(size.Width)), Math.Max(1, (int)Math.Round(size.Height)), WindowState);
        }
        else if (e.Property == WindowStateProperty)
        {
            var state = WindowState;
            var (predictedWidth, predictedHeight) = PredictedClientSize(state);
            _manager.HostStateChanged(_id, state, predictedWidth, predictedHeight);
            if (!_applyingClientState && predictedWidth == 0 && state is not WindowState.Normal)
            {
                var size = ClientSize;
                _manager.HostResized(
                    _id, Math.Max(1, (int)Math.Round(size.Width)), Math.Max(1, (int)Math.Round(size.Height)), state);
            }
        }
    }

    private (int Width, int Height) PredictedClientSize(WindowState state)
    {
        if (state != WindowState.Maximized ||
            (Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen)
        {
            return default;
        }

        var scale = DesktopScaling > 0 ? DesktopScaling : 1.0;
        return (
            Math.Max(1, (int)Math.Round(screen.WorkingArea.Width / scale)),
            Math.Max(1, (int)Math.Round(screen.WorkingArea.Height / scale)));
    }
}
