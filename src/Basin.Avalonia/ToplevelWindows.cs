using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;
using static Basin.Avalonia.AvaloniaLog;

namespace Basin.Avalonia;

public sealed class ToplevelWindows : IDisposable
{
    private const int CellStride = 16384;

    private readonly BasinCompositorHost _host;
    private readonly Action<Action> _post;
    private readonly Dictionary<int, Entry> _entries = [];
    private readonly Dictionary<int, ToplevelWindow> _windows = [];
    private readonly Stack<int> _freeCells = [];
    private readonly XdgDecorationManager? _decorations;
    private int _nextId;
    private int _nextCell;
    private bool _disposed;

    private sealed class Entry
    {
        public required int Id;
        public XdgToplevelWindow? Toplevel;
        public XdgPopupWindow? Popup;
        public IForeignToplevel? Foreign;
        public Surface? Plain;
        public required SceneSurface SceneSurface;
        public required int Cell;
        public Surface Surface => Toplevel?.Surface ?? Popup?.Surface ?? Foreign?.Surface ?? Plain!;
        public Box Geometry => Toplevel?.Xdg.EffectiveGeometry ?? Popup?.Xdg.EffectiveGeometry ?? default;
        public BasinViewOutput? View;
        public int Width;
        public int Height;
        public int RequestedWidth;
        public int RequestedHeight;
        public IEventSource? SettleTimer;
        public int WindowWidth;
        public int WindowHeight;
        public int NormalMarginWidth;
        public int NormalMarginHeight;
        public int NormalWidth;
        public int NormalHeight;
        public int CloseRequests;
        public bool WindowCreated;
        public bool ConfigurePending;
        public bool ClientDecorated;
        public LayerSurface? Layer;
        public int LayerX;
        public int LayerY;
        public HostStackingBand Band;
        public double Scale;
        public string? ScreenKey;
        public (int Left, int Top, int Right, int Bottom) Insets;
        public ScreenSurfaceKind Kind;
        public OutputGlobal? OverriddenOutput;
        public bool IsScreen => Kind == ScreenSurfaceKind.Screen;
        public (int X, int Y) ViewOrigin => ClientDecorated && !IsScreen ? (0, 0) : (Geometry.X, Geometry.Y);
    }

    private readonly BasinInputChannel _input = new();
    private readonly TouchPoints _touchPoints = new();
    private readonly Action? _requestFrame;
    private readonly Basin.Desktop.FractionalScaleManager? _fractional;
    private readonly Basin.Desktop.XdgOutputManager? _xdgOutputs;
    private readonly Basin.Desktop.PointerConstraintsManager? _constraints;
    private int _pointerEntryId;
    private Surface? _cursorSurface;
    private Action? _cursorCommitted;
    private Action? _cursorDestroyed;

    public ToplevelWindows(
        BasinCompositorHost host,
        Action<Action> postToCompositor,
        IAvaloniaShellPolicy? policy = null,
        Action? requestFrame = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(postToCompositor);
        _host = host;
        _post = postToCompositor;
        _requestFrame = requestFrame;
        TitleBarBehavior.Install();
        if (OperatingSystem.IsMacOS())
        {
            MacFullscreenSize.Install();
            MacZoomButton.Install();
        }

        Policy = policy ?? new AvaloniaShellPolicy();
        _decorations = host.Services.Find<XdgDecorationManager>();
        _fractional = host.Services.Find<Basin.Desktop.FractionalScaleManager>();
        _xdgOutputs = host.Services.Find<Basin.Desktop.XdgOutputManager>();
        _constraints = host.Services.Find<Basin.Desktop.PointerConstraintsManager>();
        if (_decorations is not null)
        {
            _decorations.DefaultMode = DecorationMode.ServerSide;
            _decorations.ChooseMode = (_, preference) => preference ?? DecorationMode.ServerSide;
            _decorations.ModeChanged += OnDecorationModeChanged;
        }

        host.Shell.NewToplevel += OnNewToplevel;
        host.Shell.NewPopup += OnNewPopup;
        _layerShell = host.Services.Find<LayerShell>();
        if (_layerShell is not null)
        {
            _layerShell.NewSurface += OnNewLayerSurface;
        }
        host.Session.BeforeDispatch += _ => DrainInput();
        if (host.Services.Find<Basin.Desktop.CursorShapeManager>() is { } shapes)
        {
            shapes.ShapeRequested += OnShapeRequested;
        }

        host.Seat.Pointer.CursorRequested += OnCursorRequested;
    }

    internal void Enqueue(in BasinInputEvent input)
    {
        _input.Write(input);
        _requestFrame?.Invoke();
    }

    private void DrainInput()
    {
        while (_input.TryRead(out var input))
        {
            DispatchInput(input);
        }
    }

    private void DispatchInput(in BasinInputEvent input)
    {
        if (!_entries.TryGetValue(input.WindowId, out var entry))
        {
            return;
        }

        var surface = entry.Surface;
        var (originX, originY) = entry.ViewOrigin;
        var pointer = _host.Seat.Pointer;
        switch (input.Kind)
        {
            case InputKind.PointerMotion:
            case InputKind.PointerEnter:
            {
                var picked = Pick(entry, input.X, input.Y);
                _pointerEntryId = entry.Id;
                pointer.NotifyMotionAt(
                    input.TimeMs,
                    picked.Hit?.Surface,
                    picked.Hit?.X ?? 0,
                    picked.Hit?.Y ?? 0,
                    picked.LayoutX,
                    picked.LayoutY);
                break;
            }
            case InputKind.PointerLeave:
                pointer.NotifyClearFocus();
                break;
            case InputKind.PointerButton:
                pointer.NotifyButton(input.TimeMs, input.Code, input.Pressed);
                break;
            case InputKind.PointerAxis:
                if (input.DeltaY != 0)
                {
                    pointer.NotifyAxis(input.TimeMs, new PointerAxis(
                        Wayland.WlPointer.Axis.VerticalScroll,
                        -input.DeltaY * 10,
                        (int)(-input.DeltaY * 120)));
                }

                if (input.DeltaX != 0)
                {
                    pointer.NotifyAxis(input.TimeMs, new PointerAxis(
                        Wayland.WlPointer.Axis.HorizontalScroll,
                        -input.DeltaX * 10,
                        (int)(-input.DeltaX * 120)));
                }

                break;
            case InputKind.Key:
                _host.Seat.Keyboard.NotifyKey(input.TimeMs, input.Code, input.Pressed);
                break;
            case InputKind.FocusIn:
                _host.Seat.Keyboard.NotifyEnter(surface);
                break;
            case InputKind.FocusOut:
                if (entry.Layer is
                    {
                        IsMapped: true,
                        KeyboardInteractivity: Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.KeyboardInteractivity.Exclusive,
                    })
                {
                    break;
                }

                _host.Seat.Keyboard.NotifyClearFocus();
                break;
            case InputKind.TouchDown:
            {
                var picked = Pick(entry, input.X, input.Y);
                if (picked.Hit is { Surface: { } touched } hit)
                {
                    _touchPoints.Down(input.TouchId, picked.LayoutX, picked.LayoutY, hit.Node);
                    _host.Seat.Touch.NotifyDown(touched, input.TimeMs, input.TouchId, hit.X, hit.Y);
                    _host.Seat.Touch.NotifyFrame();
                }

                break;
            }

            case InputKind.TouchMotion:
            {
                var layoutX = originX + input.X;
                var layoutY = entry.Cell * CellStride + originY + input.Y;
                if (_touchPoints.TryMotion(input.TouchId, layoutX, layoutY, out var localX, out var localY))
                {
                    _host.Seat.Touch.NotifyMotion(input.TimeMs, input.TouchId, localX, localY);
                    _host.Seat.Touch.NotifyFrame();
                }

                break;
            }

            case InputKind.TouchUp:
                _touchPoints.Up(input.TouchId);
                _host.Seat.Touch.NotifyUp(input.TimeMs, input.TouchId);
                _host.Seat.Touch.NotifyFrame();
                break;
        }
    }

    private (Entry Entry, SceneHit? Hit, double LayoutX, double LayoutY) Pick(Entry entry, double x, double y)
    {
        var (originX, originY) = entry.ViewOrigin;
        var layoutX = originX + x;
        var layoutY = entry.Cell * CellStride + originY + y;
        var hit = _host.Scene.SurfaceAt(layoutX, layoutY);
        if (hit is not null || entry.Layer is null)
        {
            return (entry, hit, layoutX, layoutY);
        }

        var screenX = entry.LayerX + x;
        var screenY = entry.LayerY + y;
        var ceiling = LayerRank(entry);
        Entry? best = null;
        var bestRank = int.MinValue;
        SceneHit? bestHit = null;
        var bestLayoutX = 0.0;
        var bestLayoutY = 0.0;
        for (var i = 0; i < _layers.Count; i++)
        {
            var candidate = _layers[i].Entry;
            var rank = ((int)_layers[i].Layer.Layer * LayerStride) + i;
            if (rank >= ceiling || rank <= bestRank || candidate.ScreenKey != entry.ScreenKey)
            {
                continue;
            }

            var localX = screenX - candidate.LayerX;
            var localY = screenY - candidate.LayerY;
            if (localX < 0 || localY < 0 || localX >= candidate.Width || localY >= candidate.Height)
            {
                continue;
            }

            var (candidateOriginX, candidateOriginY) = candidate.ViewOrigin;
            var candidateLayoutX = candidateOriginX + localX;
            var candidateLayoutY = (candidate.Cell * CellStride) + candidateOriginY + localY;
            if (_host.Scene.SurfaceAt(candidateLayoutX, candidateLayoutY) is not { } candidateHit)
            {
                continue;
            }

            best = candidate;
            bestRank = rank;
            bestHit = candidateHit;
            bestLayoutX = candidateLayoutX;
            bestLayoutY = candidateLayoutY;
        }

        return best is null
            ? (entry, null, layoutX, layoutY)
            : (best, bestHit, bestLayoutX, bestLayoutY);
    }

    private const int LayerStride = 1 << 16;

    private int LayerRank(Entry entry)
    {
        for (var i = 0; i < _layers.Count; i++)
        {
            if (ReferenceEquals(_layers[i].Entry, entry))
            {
                return ((int)_layers[i].Layer.Layer * LayerStride) + i;
            }
        }

        return int.MaxValue;
    }

    private void OnShapeRequested(Capabilities.CursorShape shape)
    {
        var id = _pointerEntryId;
        RunOnUi(() => ApplyCursorTo(id, AvaloniaCursor.For(shape)));
    }

    private void OnCursorRequested(global::Basin.Seat.CursorRequest request)
    {
        ReleaseCursorSurface();
        var id = _pointerEntryId;
        if (request.Surface is not { } cursorSurface)
        {
            RunOnUi(() => ApplyCursorTo(id, new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.None)));
            return;
        }

        Log.Debug($"cursor surface: buffer {cursorSurface.Current.Buffer?.Width}x{cursorSurface.Current.Buffer?.Height} scale {cursorSurface.Current.Scale} logical {cursorSurface.Current.Width}x{cursorSurface.Current.Height} hotspot {request.HotspotX},{request.HotspotY}");
        var hotspotX = request.HotspotX;
        var hotspotY = request.HotspotY;
        _cursorSurface = cursorSurface;
        _cursorCommitted = () => ApplyCursorSurface(id, cursorSurface, hotspotX, hotspotY);
        _cursorDestroyed = ReleaseCursorSurface;
        cursorSurface.Committed += _cursorCommitted;
        cursorSurface.Destroyed += _cursorDestroyed;
        ApplyCursorSurface(id, cursorSurface, hotspotX, hotspotY);
    }

    private void ApplyCursorSurface(int id, Surface surface, int hotspotX, int hotspotY)
    {
        if (AvaloniaCursor.FromSurface(surface, hotspotX, hotspotY) is not { } cursor)
        {
            return;
        }

        RunOnUi(() => ApplyCursorTo(id, cursor));
    }

    private void ReleaseCursorSurface()
    {
        if (_cursorSurface is not { } surface)
        {
            return;
        }

        surface.Committed -= _cursorCommitted;
        surface.Destroyed -= _cursorDestroyed;
        _cursorSurface = null;
        _cursorCommitted = null;
        _cursorDestroyed = null;
    }

    private void ApplyCursorTo(int id, global::Avalonia.Input.Cursor cursor)
    {
        if (_windows.TryGetValue(id, out var window))
        {
            window.ApplyCursor(cursor);
        }
        else if (_popupWindows.TryGetValue(id, out var popup))
        {
            popup.ApplyCursor(cursor);
        }
        else if (_layerWindows.TryGetValue(id, out var layer))
        {
            layer.ApplyCursor(cursor);
        }
    }

    public IAvaloniaShellPolicy Policy { get; set; }

    public IReadOnlyCollection<ToplevelWindow> Windows => _windows.Values;

    public IReadOnlyCollection<PopupWindow> PopupWindows => _popupWindows.Values;

    public IReadOnlyCollection<LayerWindow> LayerWindows => _layerWindows.Values;

    public event Action<int>? CountChanged;

    public event Action? WindowActivatedOnHost;

    public event Action<ToplevelWindow?>? ScreenWindowChanged;

    internal void NotifyActivatedUi() => WindowActivatedOnHost?.Invoke();

    public Task CloseAllAsync()
    {
        List<Task> pending = [];
        foreach (var window in _windows.Values.ToArray())
        {
            pending.Add(window.CloseFromCompositorAsync());
        }

        foreach (var window in _layerWindows.Values.ToArray())
        {
            pending.Add(window.CloseFromCompositorAsync());
        }

        return Task.WhenAll(pending);
    }

    private void OnNewToplevel(XdgToplevelWindow toplevel)
    {
        var cell = _freeCells.Count > 0 ? _freeCells.Pop() : _nextCell++;
        var entry = new Entry
        {
            Id = ++_nextId,
            Toplevel = toplevel,
            SceneSurface = new SceneSurface(_host.Scene.Root, toplevel.Surface),
            Cell = cell,
        };
        entry.SceneSurface.Tree.SetPosition(0, cell * CellStride);
        _entries[entry.Id] = entry;

        toplevel.Xdg.Mapped += () => OnMapped(entry);
        toplevel.Xdg.Unmapped += () => OnUnmapped(entry);
        toplevel.Xdg.Committed += () => OnCommitted(entry);
        toplevel.TitleChanged += () =>
        {
            var title = toplevel.Title;
            RunOnUi(() => WindowOf(entry)?.ApplyTitle(title));
        };
        toplevel.MaximizeRequested += on => RunOnUi(() => WindowOf(entry)?.ApplyState(on ? WindowState.Maximized : WindowState.Normal));
        toplevel.FullscreenRequested += on => RunOnUi(() => WindowOf(entry)?.ApplyState(on ? WindowState.FullScreen : WindowState.Normal));
        toplevel.MinimizeRequested += () => RunOnUi(() => WindowOf(entry)?.ApplyState(WindowState.Minimized));
        toplevel.MoveRequested += _ => RunOnUi(() => WindowOf(entry)?.BeginClientMove());
        toplevel.ResizeRequested += (_, edges) => RunOnUi(() => WindowOf(entry)?.BeginClientResize(edges));
        toplevel.Destroyed += () => OnDestroyed(entry);
    }

    private void OnMapped(Entry entry)
    {
        var toplevel = entry.Toplevel!;
        var geometry = toplevel.Xdg.EffectiveGeometry;
        entry.Width = Math.Max(1, geometry.Width);
        entry.Height = Math.Max(1, geometry.Height);
        if (entry.WindowCreated)
        {
            RunOnUi(() => WindowOf(entry)?.ApplyVisible(true));
            return;
        }

        entry.WindowCreated = true;
        Log.Debug($"toplevel mapped: {entry.Width}x{entry.Height} '{toplevel.Title}'");
        entry.Kind = Policy.Classify(new ToplevelInfo(
            toplevel.Title, toplevel.AppId, entry.Width, entry.Height, toplevel.Surface.Resource.Client));
        var serverSide = entry.IsScreen
            || (_decorations is not null
                && _decorations.TryGetPreference(toplevel, out var preference)
                && (preference ?? DecorationMode.ServerSide) != DecorationMode.ClientSide);
        if (entry.IsScreen)
        {
            _decorations?.SetMode(toplevel, DecorationMode.ServerSide);
        }

        entry.ClientDecorated = !serverSide;
        var current = toplevel.Surface.Current;
        entry.WindowWidth = entry.ClientDecorated ? Math.Max(1, current.Width) : entry.Width;
        entry.WindowHeight = entry.ClientDecorated ? Math.Max(1, current.Height) : entry.Height;
        var info = new ToplevelInfo(
            toplevel.Title, toplevel.AppId, entry.WindowWidth, entry.WindowHeight,
            toplevel.Surface.Resource.Client);
        var ownerId = OwnerIdOf(toplevel);
        var marginWidth = Math.Max(0, entry.WindowWidth - entry.Width);
        var marginHeight = Math.Max(0, entry.WindowHeight - entry.Height);
        if (HasFrameMargins(toplevel))
        {
            entry.NormalMarginWidth = marginWidth;
            entry.NormalMarginHeight = marginHeight;
        }

        var minimum = entry.IsScreen
            ? (0, 0)
            : serverSide
                ? (toplevel.MinWidth, toplevel.MinHeight)
                : (toplevel.MinWidth > 0 ? toplevel.MinWidth + marginWidth : 0,
                    toplevel.MinHeight > 0 ? toplevel.MinHeight + marginHeight : 0);
        var maximum = entry.IsScreen
            ? (0, 0)
            : serverSide
                ? (toplevel.MaxWidth, toplevel.MaxHeight)
                : (toplevel.MaxWidth > 0 ? toplevel.MaxWidth + marginWidth : 0,
                    toplevel.MaxHeight > 0 ? toplevel.MaxHeight + marginHeight : 0);
        entry.Insets = InsetsOf(entry, geometry, entry.WindowWidth, entry.WindowHeight);
        var insets = entry.Insets;
        var id = entry.Id;
        var isScreen = entry.IsScreen;
        ApplyScreenOutput(entry, entry.Width, entry.Height);
        RunOnUi(() =>
        {
            var window = new ToplevelWindow(this, id, info, serverSide, minimum, maximum);
            window.ApplyResizeInsets(insets);
            _windows[id] = window;
            Policy.PlaceWindow(window, info);
            if (isScreen)
            {
                _screenWindowId = id;
                ScreenWindowChanged?.Invoke(window);
            }

            if (ownerId is { } owner && _windows.TryGetValue(owner, out var ownerWindow))
            {
                window.Show(ownerWindow);
            }
            else
            {
                window.Show();
            }

            if (isScreen && OperatingSystem.IsMacOS())
            {
                MacZoomButton.UseFullScreen(window.TryGetPlatformHandle());
            }

            CountChanged?.Invoke(_windows.Count);
        });
    }

    private void OnCommitted(Entry entry)
    {
        if (entry.Toplevel is null || (entry.View is null && !entry.WindowCreated))
        {
            return;
        }

        var geometry = entry.Toplevel.Xdg.EffectiveGeometry;
        var width = Math.Max(1, geometry.Width);
        var height = Math.Max(1, geometry.Height);
        var current = entry.Toplevel.Surface.Current;
        var windowWidth = entry.ClientDecorated ? Math.Max(1, current.Width) : width;
        var windowHeight = entry.ClientDecorated ? Math.Max(1, current.Height) : height;
        var (originX, originY) = entry.ViewOrigin;
        if (entry.View is { } view)
        {
            view.SceneOutput.Position = new(originX, entry.Cell * CellStride + originY);
            if (windowWidth != view.Target.Width || windowHeight != view.Target.Height)
            {
                view.Resize(windowWidth, windowHeight, 1.0);
            }
        }

        entry.Width = width;
        entry.Height = height;
        if (entry.ClientDecorated && HasFrameMargins(entry.Toplevel))
        {
            entry.NormalMarginWidth = Math.Max(0, windowWidth - width);
            entry.NormalMarginHeight = Math.Max(0, windowHeight - height);
        }

        var insets = InsetsOf(entry, geometry, windowWidth, windowHeight);
        if (insets != entry.Insets)
        {
            entry.Insets = insets;
            RunOnUi(() => WindowOf(entry)?.ApplyResizeInsets(insets));
        }

        if (entry.Scale > 0)
        {
            AnnounceScaleTree(entry.Surface, entry.Scale);
        }

        _host.Screens.RefreshPresence(entry.Surface, entry.ScreenKey);
        if (width == entry.RequestedWidth && height == entry.RequestedHeight)
        {
            entry.ConfigurePending = false;
        }

        if (windowWidth == entry.WindowWidth && windowHeight == entry.WindowHeight)
        {
            return;
        }

        var converged = width == entry.RequestedWidth && height == entry.RequestedHeight;
        var followHost = converged || !HasFrameMargins(entry.Toplevel);
        entry.WindowWidth = windowWidth;
        entry.WindowHeight = windowHeight;
        Log.Debug($"toplevel commit: geometry {geometry.X},{geometry.Y} {width}x{height} window {windowWidth}x{windowHeight} followHost={followHost}");
        if (followHost)
        {
            entry.SettleTimer?.UpdateTimer(0);
        }
        else
        {
            entry.SettleTimer ??= _host.Loop.AddTimer(() => OnSettle(entry));
            entry.SettleTimer.UpdateTimer(SettleDelayMillis);
        }
    }

    private const int SettleDelayMillis = 150;

    private void OnSettle(Entry entry)
    {
        if (entry.Toplevel is not { } toplevel ||
            !HasFrameMargins(toplevel) ||
            (entry.Width == entry.RequestedWidth && entry.Height == entry.RequestedHeight))
        {
            return;
        }

        if (entry.ConfigurePending)
        {
            return;
        }

        if (toplevel.Xdg.HasUnackedConfigure)
        {
            entry.SettleTimer?.UpdateTimer(SettleDelayMillis);
            return;
        }

        var windowWidth = entry.WindowWidth;
        var windowHeight = entry.WindowHeight;
        Log.Debug($"toplevel settle: window {windowWidth}x{windowHeight}");
        RunOnUi(() => WindowOf(entry)?.ApplyClientSize(windowWidth, windowHeight));
    }

    private void OnUnmapped(Entry entry)
    {
        Log.Debug($"toplevel unmapped: '{entry.Toplevel?.Title}'");
        ClearScreenOutput(entry);
        RunOnUi(() => WindowOf(entry)?.ApplyVisible(false));
    }

    private void OnDestroyed(Entry entry)
    {
        Log.Debug($"toplevel destroyed: '{entry.Toplevel?.Title}'");
        entry.SettleTimer?.Remove();
        entry.SettleTimer = null;
        ClearScreenOutput(entry);
        ReleaseHeldInput(entry.Surface);
        _entries.Remove(entry.Id);
        _freeCells.Push(entry.Cell);
        var wasScreen = entry.IsScreen;
        RunOnUi(() =>
        {
            if (_windows.Remove(entry.Id, out var window))
            {
                if (wasScreen && _screenWindowId == entry.Id)
                {
                    _screenWindowId = 0;
                    ScreenWindowChanged?.Invoke(null);
                    if (OperatingSystem.IsMacOS())
                    {
                        MacZoomButton.Forget(window.TryGetPlatformHandle());
                    }
                }

                _ = window.CloseFromCompositorAsync();
                CountChanged?.Invoke(_windows.Count);
            }
        });
    }

    private void OnDecorationModeChanged(XdgToplevelWindow toplevel, DecorationMode mode)
    {
        foreach (var entry in _entries.Values)
        {
            if (ReferenceEquals(entry.Toplevel, toplevel))
            {
                if (entry.IsScreen)
                {
                    _decorations?.SetMode(toplevel, DecorationMode.ServerSide);
                    return;
                }

                var serverSide = mode != DecorationMode.ClientSide;
                entry.ClientDecorated = !serverSide;
                entry.WindowWidth = 0;
                entry.WindowHeight = 0;
                RunOnUi(() => WindowOf(entry)?.ApplyDecorations(serverSide));
                if (entry.WindowCreated)
                {
                    OnCommitted(entry);
                }

                return;
            }
        }
    }

    private static bool HasFrameMargins(XdgToplevelWindow toplevel) =>
        !toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized) &&
        !toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen);

    private static (int Left, int Top, int Right, int Bottom) InsetsOf(
        Entry entry, Box geometry, int windowWidth, int windowHeight)
    {
        if (entry.IsScreen || !entry.ClientDecorated || entry.Toplevel is not { } toplevel || !HasFrameMargins(toplevel))
        {
            return default;
        }

        return (
            Math.Max(0, geometry.X),
            Math.Max(0, geometry.Y),
            Math.Max(0, windowWidth - entry.Width - geometry.X),
            Math.Max(0, windowHeight - entry.Height - geometry.Y));
    }

    private int? OwnerIdOf(XdgToplevelWindow toplevel)
    {
        if (toplevel.Parent is null)
        {
            return null;
        }

        foreach (var entry in _entries.Values)
        {
            if (ReferenceEquals(entry.Toplevel, toplevel.Parent))
            {
                return entry.Id;
            }
        }

        return null;
    }

    internal BasinViewOutput CreateView(int id)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            throw new InvalidOperationException("The toplevel is gone.");
        }

        var (originX, originY) = entry.ViewOrigin;
        var view = _host.CreateViewOutput(Math.Max(1, entry.WindowWidth), Math.Max(1, entry.WindowHeight));
        view.SceneOutput.Position = new(originX, entry.Cell * CellStride + originY);
        entry.View = view;
        return view;
    }

    internal BasinCompositorHost Host => _host;

    internal void HostResized(int id, int width, int height, WindowState state)
    {
        _post(() =>
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                ResizeCore(entry, width, height, state);
            }
        });
    }

    private void ResizeCore(Entry entry, int width, int height, WindowState state)
    {
        if (state is WindowState.Normal or WindowState.Maximized or WindowState.FullScreen)
        {
            entry.Toplevel?.SetMaximized(state == WindowState.Maximized);
            entry.Toplevel?.SetFullscreen(state == WindowState.FullScreen);
        }

        if (state == WindowState.Normal)
        {
            entry.NormalWidth = width;
            entry.NormalHeight = height;
        }

        if (entry.ClientDecorated && state is not (WindowState.FullScreen or WindowState.Maximized))
        {
            width = Math.Max(1, width - entry.NormalMarginWidth);
            height = Math.Max(1, height - entry.NormalMarginHeight);
        }

        if (width == entry.RequestedWidth && height == entry.RequestedHeight)
        {
            return;
        }

        if (entry.Toplevel is { } toplevel)
        {
            entry.ConfigurePending = true;
            entry.RequestedWidth = width;
            entry.RequestedHeight = height;
            toplevel.SetSize(width, height);
            ApplyScreenOutput(entry, width, height);
            toplevel.RequestConfigure();
        }
        else if (entry.Foreign is { } foreign)
        {
            entry.ConfigurePending = true;
            entry.RequestedWidth = width;
            entry.RequestedHeight = height;
            foreign.Resize(width, height);
        }
    }

    internal void ClientResizeStateChanged(int id, bool resizing)
    {
        _post(() =>
        {
            if (_entries.TryGetValue(id, out var entry) && entry.Toplevel is { } toplevel)
            {
                toplevel.SetResizing(resizing);
                toplevel.RequestConfigure();
            }
        });
    }

    internal void HostStateChanged(int id, WindowState state, int predictedWidth = 0, int predictedHeight = 0)
    {
        _post(() =>
        {
            if (!_entries.TryGetValue(id, out var entry) || entry.Toplevel is not { } toplevel)
            {
                return;
            }

            toplevel.SetMaximized(state == WindowState.Maximized);
            toplevel.SetFullscreen(state == WindowState.FullScreen);
            toplevel.SetSuspended(state == WindowState.Minimized);
            var (width, height) = (predictedWidth, predictedHeight);
            if (state == WindowState.Normal && entry.NormalWidth > 0)
            {
                (width, height) = (entry.NormalWidth, entry.NormalHeight);
            }

            if (width > 0 && height > 0)
            {
                ResizeCore(entry, width, height, state);
            }
        });
    }

    internal void HostActivated(int id, bool active)
    {
        if (!active)
        {
            DispatcherTimer.RunOnce(() =>
            {
                foreach (var window in _windows.Values)
                {
                    if (window.IsActive)
                    {
                        return;
                    }
                }

                DeactivateNow(id);
            }, TimeSpan.FromMilliseconds(120));
            return;
        }

        _post(() =>
        {
            if (!_entries.TryGetValue(id, out var entry))
            {
                return;
            }

            if (entry.Toplevel is { } toplevel)
            {
                toplevel.SetActivated(true);
                _host.Seat.Keyboard.NotifyEnter(toplevel.Surface);
            }
            else if (entry.Foreign is { } foreign)
            {
                foreign.Activate(true);
                _host.Seat.Keyboard.NotifyEnter(foreign.Surface);
            }
        });
    }

    internal void DeactivateNow(int id)
    {
        _post(() =>
        {
            if (!_entries.TryGetValue(id, out var entry))
            {
                return;
            }

            entry.Foreign?.Activate(false);
            if (entry.Toplevel is { } toplevel || entry.Foreign is not null)
            {
                entry.Toplevel?.SetActivated(false);
                if (ReferenceEquals(_host.Seat.Keyboard.Focus, entry.Surface))
                {
                    _host.Seat.Keyboard.NotifyClearFocus();
                }

                foreach (var other in _entries.Values.ToArray())
                {
                    if (other.Popup is { HasGrab: true } grabbed)
                    {
                        grabbed.Dismiss();
                    }
                }
            }
        });
    }

    internal void HostScaleChanged(int id, double scale, bool authoritative)
    {
        _post(() =>
        {
            if (!_entries.TryGetValue(id, out var entry))
            {
                return;
            }

            var value = authoritative
                ? scale
                : entry.ScreenKey is { } key ? _host.Screens.ScalingOf(key) : _host.Screens.DefaultScaling;
            if (value <= 0)
            {
                return;
            }

            entry.Scale = value;
            AnnounceScaleTree(entry.Surface, value);
            ApplyScreenOutput(entry);
        });
    }

    private void AnnounceScaleTree(Surface surface, double scale)
    {
        _fractional?.AnnounceScale(surface, scale);
        foreach (var below in surface.SubsurfacesBelow)
        {
            AnnounceScaleTree(below.Surface, scale);
        }

        foreach (var above in surface.SubsurfacesAbove)
        {
            AnnounceScaleTree(above.Surface, scale);
        }
    }

    internal void HostScreenScaleObserved(string key, double scale)
    {
        _post(() => _host.Screens.NoteWindowScale(key, scale));
    }

    internal void HostScreenChanged(int id, string? key)
    {
        _post(() =>
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                entry.ScreenKey = key;
                _host.Screens.EnterScreen(entry.Surface, key);
                if (key is not null)
                {
                    entry.Scale = _host.Screens.ScalingOf(key);
                }

                ApplyScreenOutput(entry);
            }
        });
    }

    internal void HostCloseRequested(int id)
    {
        _post(() =>
        {
            if (!_entries.TryGetValue(id, out var entry))
            {
                return;
            }

            if (entry.Toplevel is { } toplevel)
            {
                entry.CloseRequests++;
                Policy.CloseRequested(toplevel, entry.CloseRequests);
            }
            else if (entry.Foreign is { } foreign)
            {
                foreign.Close();
            }
            else if (LayerOf(id) is { } layer)
            {
                layer.Close();
            }
        });
    }

    private LayerSurface? LayerOf(int id)
    {
        foreach (var row in _layers)
        {
            if (row.Entry.Id == id)
            {
                return row.Layer;
            }
        }

        return null;
    }

    private readonly Dictionary<int, PopupWindow> _popupWindows = [];

    private void OnNewPopup(XdgPopupWindow popup)
    {
        var cell = _freeCells.Count > 0 ? _freeCells.Pop() : _nextCell++;
        var entry = new Entry
        {
            Id = ++_nextId,
            Popup = popup,
            SceneSurface = new SceneSurface(_host.Scene.Root, popup.Surface),
            Cell = cell,
            ClientDecorated = true,
        };
        entry.SceneSurface.Tree.SetPosition(0, cell * CellStride);
        _entries[entry.Id] = entry;

        if (ParentEntryIdOf(popup) is { } popupParentId && _entries.TryGetValue(popupParentId, out var popupParent))
        {
            entry.ScreenKey = popupParent.ScreenKey;
            _host.Screens.EnterScreen(popup.Surface, popupParent.ScreenKey);
            if (popupParent.Scale > 0)
            {
                entry.Scale = popupParent.Scale;
                AnnounceScaleTree(popup.Surface, popupParent.Scale);
            }
        }

        popup.Xdg.Mapped += () => OnPopupMapped(entry);
        popup.Xdg.Committed += () => OnPopupCommitted(entry);
        popup.GeometryChanged += () => PlacePopup(entry);
        popup.Repositioned += () => PlacePopup(entry);
        popup.Xdg.Unmapped += () => RunOnUi(() =>
        {
            if (_popupWindows.TryGetValue(entry.Id, out var window))
            {
                window.Hide();
            }
        });
        popup.Destroyed += () => OnPopupDestroyed(entry);
    }

    private int? ParentEntryIdOf(XdgPopupWindow popup)
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.Toplevel?.Xdg == popup.Parent || entry.Popup?.Xdg == popup.Parent)
            {
                return entry.Id;
            }
        }

        return null;
    }

    private void OnPopupMapped(Entry entry)
    {
        var popup = entry.Popup!;
        var geometry = popup.Xdg.EffectiveGeometry;
        entry.Width = Math.Max(1, geometry.Width);
        entry.Height = Math.Max(1, geometry.Height);
        var current = popup.Surface.Current;
        entry.WindowWidth = Math.Max(1, current.Width);
        entry.WindowHeight = Math.Max(1, current.Height);
        if (entry.WindowCreated)
        {
            PlacePopup(entry);
            return;
        }

        entry.WindowCreated = true;
        var id = entry.Id;
        var parentId = ParentEntryIdOf(popup);
        RunOnUi(() =>
        {
            var window = new PopupWindow(this, id);
            _popupWindows[id] = window;
        });
        PlacePopup(entry);
    }

    private void OnPopupCommitted(Entry entry)
    {
        if (entry.View is null && !entry.WindowCreated)
        {
            return;
        }

        var popup = entry.Popup!;
        var geometry = popup.Xdg.EffectiveGeometry;
        var width = Math.Max(1, geometry.Width);
        var height = Math.Max(1, geometry.Height);
        var current = popup.Surface.Current;
        var windowWidth = Math.Max(1, current.Width);
        var windowHeight = Math.Max(1, current.Height);
        if (entry.Scale > 0)
        {
            AnnounceScaleTree(entry.Surface, entry.Scale);
        }

        _host.Screens.RefreshPresence(entry.Surface, entry.ScreenKey);
        if (entry.View is { } view)
        {
            view.SceneOutput.Position = new(0, entry.Cell * CellStride);
            if (windowWidth != view.Target.Width || windowHeight != view.Target.Height)
            {
                view.Resize(windowWidth, windowHeight, 1.0);
            }
        }

        if (width != entry.Width || height != entry.Height ||
            windowWidth != entry.WindowWidth || windowHeight != entry.WindowHeight)
        {
            entry.Width = width;
            entry.Height = height;
            entry.WindowWidth = windowWidth;
            entry.WindowHeight = windowHeight;
            PlacePopup(entry);
        }
    }

    private void PlacePopup(Entry entry)
    {
        var popup = entry.Popup!;
        var parentId = ParentEntryIdOf(popup);
        var position = popup.Geometry;
        var width = Math.Max(1, entry.WindowWidth);
        var height = Math.Max(1, entry.WindowHeight);
        var popupGeometry = popup.Xdg.EffectiveGeometry;
        var parentOffset = (X: 0, Y: 0);
        if (parentId is { } parentEntryId && _entries.TryGetValue(parentEntryId, out var parentEntry)
            && parentEntry.ClientDecorated)
        {
            var parentGeometry = parentEntry.Geometry;
            parentOffset = (parentGeometry.X, parentGeometry.Y);
        }

        var anchorX = parentOffset.X + position.X - popupGeometry.X;
        var anchorY = parentOffset.Y + position.Y - popupGeometry.Y;
        var rootId = RootToplevelIdOf(parentId);
        var id = entry.Id;
        RunOnUi(() =>
        {
            if (!_popupWindows.TryGetValue(id, out var window))
            {
                return;
            }

            var parentView = parentId is { } parent ? WindowOrPopupView(parent) : null;
            window.SetContentSize(width, height);
            if (parentView is null || global::Avalonia.Controls.TopLevel.GetTopLevel(parentView) is null)
            {
                return;
            }

            window.PlaceAt(parentView, anchorX, anchorY);
            if (rootId is { } root && _windows.TryGetValue(root, out var rootWindow) &&
                rootWindow.Screens.ScreenFromTopLevel(rootWindow) is { } screen)
            {
                var viewOrigin = parentView.PointToScreen(default);
                var area = screen.WorkingArea;
                var constraint = new Box(
                    area.X - viewOrigin.X - parentOffset.X,
                    area.Y - viewOrigin.Y - parentOffset.Y,
                    area.Width,
                    area.Height);
                _post(() =>
                {
                    if (_entries.ContainsKey(id))
                    {
                        popup.Unconstrain(constraint);
                    }
                });
            }
        });
    }

    private int? RootToplevelIdOf(int? entryId)
    {
        while (entryId is { } id && _entries.TryGetValue(id, out var entry))
        {
            if (entry.Toplevel is not null)
            {
                return id;
            }

            if (entry.Popup is { } popup)
            {
                entryId = ParentEntryIdOf(popup);
                continue;
            }

            return null;
        }

        return null;
    }

    private void OnPopupDestroyed(Entry entry)
    {
        _entries.Remove(entry.Id);
        _freeCells.Push(entry.Cell);
        var view = entry.View;
        RunOnUi(() =>
        {
            if (_popupWindows.Remove(entry.Id, out var window))
            {
                _ = window.CloseFromCompositorAsync();
            }
        });
    }

    private readonly LayerShell? _layerShell;
    private readonly Dictionary<int, LayerWindow> _layerWindows = [];
    private readonly List<(LayerSurface Layer, Entry Entry)> _layers = [];

    private void OnNewLayerSurface(LayerSurface layer)
    {
        var cell = _freeCells.Count > 0 ? _freeCells.Pop() : _nextCell++;
        var entry = new Entry
        {
            Id = ++_nextId,
            Plain = layer.Surface,
            SceneSurface = new SceneSurface(_host.Scene.Root, layer.Surface),
            Cell = cell,
            ClientDecorated = true,
        };
        entry.SceneSurface.Tree.SetPosition(0, cell * CellStride);
        entry.Layer = layer;
        _entries[entry.Id] = entry;
        _layers.Add((layer, entry));
        if (layer.Output is null
            && (Policy.ChooseScreen(_host.Screens.Current) ?? _host.Screens.DefaultKey) is { } chosen)
        {
            layer.Output = _host.Screens.GlobalFor(chosen);
        }

        layer.InitialCommit += ArrangeLayers;
        layer.Mapped += () => OnLayerMapped(layer, entry);
        layer.Committed += () => OnLayerCommitted(layer, entry);
        layer.Unmapped += () => OnLayerUnmapped(layer, entry);
        layer.Destroyed += () => OnLayerDestroyed(layer, entry);
        layer.Surface.Destroyed += () => OnLayerDestroyed(layer, entry);
    }

    internal void ReleaseHeldInput(Surface surface) => _post(() =>
    {
        var keyboard = _host.Seat.Keyboard;
        if (keyboard.PressedKeys.Count > 0 && FocusWentWith(keyboard.Focus, surface))
        {
            foreach (var key in keyboard.PressedKeys.ToArray())
            {
                keyboard.NotifyKeyConsumed(key, pressed: false);
            }
        }

        var pointer = _host.Seat.Pointer;
        if (pointer.HasImplicitGrab && FocusWentWith(pointer.Focus, surface))
        {
            pointer.ClearImplicitGrab();
        }
    });

    private static bool FocusWentWith(Surface? focus, Surface surface) =>
        focus is null || focus.IsDestroyed || ReferenceEquals(focus, surface);

    private void ArrangeLayers()
    {
        if (_layers.Count == 0)
        {
            return;
        }

        foreach (var screen in _host.Screens.Current)
        {
            var onScreen = new List<(LayerSurface Layer, Entry Entry)>();
            foreach (var row in _layers)
            {
                if (ScreenKeyOf(row.Layer) == screen.Key)
                {
                    onScreen.Add(row);
                }
            }

            if (onScreen.Count == 0)
            {
                continue;
            }

            var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
            var bounds = new Box(
                0,
                0,
                Math.Max(1, (int)Math.Round(screen.Width / scale)),
                Math.Max(1, (int)Math.Round(screen.Height / scale)));
            var surfaces = new LayerSurface[onScreen.Count];
            for (var i = 0; i < onScreen.Count; i++)
            {
                surfaces[i] = onScreen[i].Layer;
            }

            var (placements, _) = LayerLayout.Arrange(bounds, surfaces);
            foreach (var placement in placements)
            {
                var (layer, entry) = onScreen[placement.Index];
                var width = Math.Max(1, placement.Box.Width);
                var height = Math.Max(1, placement.Box.Height);
                entry.ScreenKey = screen.Key;
                entry.LayerX = placement.Box.X;
                entry.LayerY = placement.Box.Y;
                entry.Width = width;
                entry.Height = height;
                entry.WindowWidth = width;
                entry.WindowHeight = height;
                _host.Screens.EnterScreen(layer.Surface, screen.Key);
                layer.Configure(width, height);
                var position = new global::Avalonia.PixelPoint(
                    screen.X + (int)Math.Round(placement.Box.X * scale),
                    screen.Y + (int)Math.Round(placement.Box.Y * scale));
                var id = entry.Id;
                RunOnUi(() =>
                {
                    if (_layerWindows.TryGetValue(id, out var window))
                    {
                        window.PlaceAt(position, width, height);
                    }
                });
            }
        }
    }

    private static string DescribeRegion(Pixman.PixmanRegion32 region)
    {
        var text = new System.Text.StringBuilder();
        foreach (var rect in RegionRects.Of(region))
        {
            text.Append($"[{rect.X1},{rect.Y1} {rect.X2 - rect.X1}x{rect.Y2 - rect.Y1}]");
        }

        return text.ToString();
    }

    private string? ScreenKeyOf(LayerSurface layer) => _host.Screens.KeyOf(layer.Output);

    private void OnLayerMapped(LayerSurface layer, Entry entry)
    {
        ArrangeLayers();
        var id = entry.Id;
        var takesKeyboard = layer.KeyboardInteractivity !=
            Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.KeyboardInteractivity.None;
        var band = HostStacking.BandFor(layer.Layer);
        var remapped = entry.WindowCreated;
        entry.WindowCreated = true;
        entry.Band = band;
        Log.Debug(
            $"layer {(remapped ? "remapped" : "mapped")}: '{layer.Namespace}' id={id} {layer.Layer} " +
            $"keyboard={layer.KeyboardInteractivity} anchor={layer.Anchor} zone={layer.ExclusiveZone} " +
            $"{entry.Width}x{entry.Height} inputInfinite={layer.Surface.Current.InputIsInfinite} " +
            $"inputRects={layer.Surface.Current.Input.RectangleCount} {DescribeRegion(layer.Surface.Current.Input)}");
        RunOnUi(() =>
        {
            LayerWindow window;
            if (remapped)
            {
                if (!_layerWindows.TryGetValue(id, out var existing))
                {
                    return;
                }

                window = existing;
                window.SetTakesKeyboard(takesKeyboard);
                window.SetBand(band);
            }
            else
            {
                window = new LayerWindow(this, id, takesKeyboard, band);
                _layerWindows[id] = window;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }

            if (takesKeyboard)
            {
                window.Activate();
            }
        });
        ArrangeLayers();
    }

    private void OnLayerCommitted(LayerSurface layer, Entry entry)
    {
        if (entry.Scale > 0)
        {
            AnnounceScaleTree(entry.Surface, entry.Scale);
        }

        var band = HostStacking.BandFor(layer.Layer);
        if (band != entry.Band)
        {
            entry.Band = band;
            var bandId = entry.Id;
            RunOnUi(() =>
            {
                if (_layerWindows.TryGetValue(bandId, out var window))
                {
                    window.SetBand(band);
                }
            });
        }

        _host.Screens.RefreshPresence(entry.Surface, entry.ScreenKey);
        var current = layer.Surface.Current;
        var width = Math.Max(1, current.Width);
        var height = Math.Max(1, current.Height);
        if (entry.View is { } view)
        {
            view.SceneOutput.Position = new(0, entry.Cell * CellStride);
            if (width != view.Target.Width || height != view.Target.Height)
            {
                view.Resize(width, height, 1.0);
            }
        }

        if (width != entry.WindowWidth || height != entry.WindowHeight)
        {
            ArrangeLayers();
        }
    }

    private void OnLayerUnmapped(LayerSurface layer, Entry entry)
    {
        Log.Debug($"layer unmapped: '{layer.Namespace}' id={entry.Id}");
        ReleaseHeldInput(entry.Surface);
        if (ReferenceEquals(_host.Seat.Keyboard.Focus, entry.Surface))
        {
            _host.Seat.Keyboard.NotifyClearFocus();
        }

        var id = entry.Id;
        RunOnUi(() =>
        {
            if (_layerWindows.TryGetValue(id, out var window))
            {
                window.Hide();
            }
        });
    }

    private void OnLayerDestroyed(LayerSurface layer, Entry entry)
    {
        if (!_entries.Remove(entry.Id))
        {
            return;
        }

        Log.Debug($"layer destroyed: '{layer.Namespace}' id={entry.Id}");

        ReleaseHeldInput(entry.Surface);
        _layers.RemoveAll(row => row.Entry.Id == entry.Id);
        if (entry.SceneSurface is { IsDestroyed: false } sceneSurface)
        {
            sceneSurface.Destroy();
        }

        _freeCells.Push(entry.Cell);
        var id = entry.Id;
        RunOnUi(() =>
        {
            if (_layerWindows.Remove(id, out var window))
            {
                _ = window.CloseFromCompositorAsync();
            }
        });
        ArrangeLayers();
    }

    public int AddForeign(IForeignToplevel foreign)
    {
        ArgumentNullException.ThrowIfNull(foreign);
        var cell = _freeCells.Count > 0 ? _freeCells.Pop() : _nextCell++;
        var entry = new Entry
        {
            Id = ++_nextId,
            Foreign = foreign,
            SceneSurface = new SceneSurface(_host.Scene.Root, foreign.Surface),
            Cell = cell,
        };
        entry.SceneSurface.Tree.SetPosition(0, cell * CellStride);
        entry.Width = Math.Max(1, foreign.Width);
        entry.Height = Math.Max(1, foreign.Height);
        entry.WindowWidth = entry.Width;
        entry.WindowHeight = entry.Height;
        entry.WindowCreated = true;
        _entries[entry.Id] = entry;
        var id = entry.Id;

        foreign.TitleChanged += () =>
        {
            var title = foreign.Title;
            RunOnUi(() => WindowOf(entry)?.ApplyTitle(title));
        };
        foreign.GeometryChanged += () => OnForeignGeometry(entry);
        foreign.Surface.Committed += () => OnForeignGeometry(entry);
        foreign.Closed += () => RemoveForeign(id);

        if (foreign.IsPopup)
        {
            var anchorId = foreign.AnchorSurface is { } anchor ? EntryIdOfSurface(anchor) : 0;
            var offsetX = foreign.AnchorOffsetX;
            var offsetY = foreign.AnchorOffsetY;
            var width = entry.Width;
            var height = entry.Height;
            RunOnUi(() =>
            {
                var window = new PopupWindow(this, id);
                window.SetContentSize(width, height);
                _popupWindows[id] = window;
                var anchorView = anchorId != 0 ? WindowOrPopupView(anchorId) : null;
                anchorView ??= _windows.Values.FirstOrDefault()?.View;
                if (anchorView is not null &&
                    global::Avalonia.Controls.TopLevel.GetTopLevel(anchorView) is not null)
                {
                    window.PlaceAt(anchorView, offsetX, offsetY);
                }
            });
        }
        else
        {
            var info = new ToplevelInfo(foreign.Title, foreign.AppId, entry.Width, entry.Height);
            var serverSide = foreign.ServerDecorated;
            RunOnUi(() =>
            {
                var window = new ToplevelWindow(this, id, info, serverSide, (0, 0), (0, 0));
                _windows[id] = window;
                Policy.PlaceWindow(window, info);
                window.Show();
                CountChanged?.Invoke(_windows.Count);
            });
        }

        return id;
    }

    public void RemoveForeign(int id)
    {
        if (!_entries.Remove(id, out var entry) || entry.Foreign is null)
        {
            if (entry is not null)
            {
                _entries[id] = entry;
            }

            return;
        }

        _freeCells.Push(entry.Cell);
        entry.SceneSurface.Destroy();
        RunOnUi(() =>
        {
            if (_windows.Remove(id, out var window))
            {
                _ = window.CloseFromCompositorAsync();
                CountChanged?.Invoke(_windows.Count);
            }

            if (_popupWindows.Remove(id, out var popup))
            {
                _ = popup.CloseFromCompositorAsync();
            }
        });
    }

    private int EntryIdOfSurface(Surface surface)
    {
        foreach (var entry in _entries.Values)
        {
            if (ReferenceEquals(entry.Surface, surface))
            {
                return entry.Id;
            }
        }

        return 0;
    }

    private global::Avalonia.Controls.Control? WindowOrPopupView(int id)
    {
        if (_windows.TryGetValue(id, out var window))
        {
            return window.View;
        }

        if (_popupWindows.TryGetValue(id, out var popup))
        {
            return popup.View;
        }

        return _layerWindows.TryGetValue(id, out var layer) ? layer.View : null;
    }

    private void OnForeignGeometry(Entry entry)
    {
        if (entry.Foreign is not { } foreign)
        {
            return;
        }

        var width = Math.Max(1, foreign.Width);
        var height = Math.Max(1, foreign.Height);
        if (entry.View is { } view && (width != view.Target.Width || height != view.Target.Height))
        {
            view.Resize(width, height, 1.0);
        }

        if (width == entry.Width && height == entry.Height)
        {
            return;
        }

        var followHost = entry.ConfigurePending;
        entry.ConfigurePending = false;
        entry.Width = width;
        entry.Height = height;
        entry.WindowWidth = width;
        entry.WindowHeight = height;
        if (!followHost)
        {
            RunOnUi(() =>
            {
                WindowOf(entry)?.ApplyClientSize(width, height);
                if (_popupWindows.TryGetValue(entry.Id, out var popup))
                {
                    popup.SetContentSize(width, height);
                }
            });
        }
    }

    private HostDrag? _drag;
    private DragIconWindow? _dragIconWindow;
    private int _dragIconEntryId;

    public HostDrag? Drag => _drag;

    public AvaloniaTextInput? TextInput { get; private set; }

    public void AttachTextInput(AvaloniaTextInput textInput)
    {
        ArgumentNullException.ThrowIfNull(textInput);
        TextInput = textInput;
        textInput.IdResolver = surface =>
        {
            foreach (var entry in _entries.Values)
            {
                if (ReferenceEquals(entry.Toplevel?.Surface, surface) ||
                    ReferenceEquals(entry.Popup?.Surface, surface))
                {
                    return entry.Id;
                }
            }

            return 0;
        };
        textInput.UiWindowResolver = id => _windows.TryGetValue(id, out var window) ? window : null;
    }

    public void AttachDrag(HostDrag drag)
    {
        ArgumentNullException.ThrowIfNull(drag);
        _drag = drag;
        drag.ClientDragStarted += OnClientDragStarted;
        drag.ClientDragEnded += OnClientDragEnded;
    }

    private void OnClientDragStarted(Surface? icon)
    {
        if (icon is null)
        {
            return;
        }

        var cell = _freeCells.Count > 0 ? _freeCells.Pop() : _nextCell++;
        var entry = new Entry
        {
            Id = ++_nextId,
            Plain = icon,
            SceneSurface = new SceneSurface(_host.Scene.Root, icon),
            Cell = cell,
        };
        entry.SceneSurface.Tree.SetPosition(0, cell * CellStride);
        entry.Width = Math.Max(1, icon.Current.Buffer?.Width ?? 32);
        entry.Height = Math.Max(1, icon.Current.Buffer?.Height ?? 32);
        entry.WindowWidth = entry.Width;
        entry.WindowHeight = entry.Height;
        _entries[entry.Id] = entry;
        _dragIconEntryId = entry.Id;
        icon.Committed += () =>
        {
            if (_entries.TryGetValue(entry.Id, out var live) && live.View is { } view &&
                icon.Current.Buffer is { } buffer)
            {
                view.Resize(Math.Max(1, buffer.Width), Math.Max(1, buffer.Height), 1.0);
            }
        };
        var id = entry.Id;
        var width = entry.Width;
        var height = entry.Height;
        RunOnUi(() =>
        {
            _dragIconWindow = new DragIconWindow(this, id, width, height);
            _dragIconWindow.Show();
        });
    }

    private void OnClientDragEnded()
    {
        var id = _dragIconEntryId;
        _dragIconEntryId = 0;
        if (id != 0 && _entries.Remove(id, out var entry))
        {
            _freeCells.Push(entry.Cell);
            entry.SceneSurface.Destroy();
        }

        RunOnUi(() =>
        {
            if (_dragIconWindow is { } window)
            {
                _dragIconWindow = null;
                _ = window.CloseFromCompositorAsync();
            }
        });
    }

    internal void MoveDragIcon(global::Avalonia.PixelPoint screen) => _dragIconWindow?.PlaceAt(screen);

    internal bool HasDragIcon => _dragIconWindow is not null;

    internal string? ClientDragHandoffText() =>
        _drag is { ClientDragActive: true } drag ? drag.TakeClientDragText() : null;

    internal void FinishClientDrag(bool dropped) => _post(() => _drag?.EndClientDrag(dropped));

    internal void HostDragEnter(int id, double x, double y, List<(string Mime, byte[] Data)> payload)
    {
        _post(() =>
        {
            if (_entries.TryGetValue(id, out var entry) && _drag is { } drag)
            {
                var (originX, originY) = entry.ViewOrigin;
                drag.EnterFromHost(entry.Surface, originX + x, originY + y, payload);
            }
        });
    }

    internal void HostDragMotion(int id, double x, double y)
    {
        _post(() =>
        {
            if (_entries.TryGetValue(id, out var entry) && _drag is { } drag)
            {
                var (originX, originY) = entry.ViewOrigin;
                drag.MotionFromHost(entry.Surface, originX + x, originY + y);
            }
        });
    }

    internal void HostDragDrop() => _post(() => _drag?.DropFromHost());

    internal void HostDragLeave() => _post(() => _drag?.LeaveFromHost());

    private void ApplyScreenOutput(Entry entry, int logicalWidth, int logicalHeight)
    {
        if (!entry.IsScreen)
        {
            return;
        }

        var key = entry.ScreenKey ?? _host.Screens.DefaultKey;
        var global = key is null ? null : _host.Screens.GlobalFor(key);
        if (global is null)
        {
            ClearScreenOutput(entry);
            return;
        }

        if (entry.OverriddenOutput is { } previous && !ReferenceEquals(previous, global))
        {
            ClearScreenOutput(entry);
        }

        var client = entry.Surface.Resource.Client;
        var scale = entry.Scale > 0 ? entry.Scale : key is null ? 1.0 : _host.Screens.ScalingOf(key);
        var width = Math.Max(1, logicalWidth);
        var height = Math.Max(1, logicalHeight);
        var mode = new OutputMode(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)),
            global.Output.CurrentMode.RefreshMilliHz);
        global.SetClientOverride(client, mode, OutputScaling.CeilScale(scale));
        var box = _host.Layout.BoxOf(global.Output);
        _xdgOutputs?.SetClientOverride(client, global, new Box(box.X, box.Y, width, height));
        entry.OverriddenOutput = global;
    }

    private void ApplyScreenOutput(Entry entry) => ApplyScreenOutput(
        entry,
        entry.RequestedWidth > 0 ? entry.RequestedWidth : entry.Width,
        entry.RequestedHeight > 0 ? entry.RequestedHeight : entry.Height);

    private void ClearScreenOutput(Entry entry)
    {
        if (entry.OverriddenOutput is not { } global)
        {
            return;
        }

        entry.OverriddenOutput = null;
        if (!entry.Surface.IsDestroyed)
        {
            var client = entry.Surface.Resource.Client;
            global.ClearClientOverride(client);
            _xdgOutputs?.ClearClientOverride(client, global);
        }
    }

    public bool InputCaptured { get; private set; }

    public void CaptureInput(bool captured)
    {
        _post(() =>
        {
            InputCaptured = captured;
            foreach (var entry in _entries.Values)
            {
                if (!entry.IsScreen || _constraints?.ConstraintFor(entry.Surface) is not { } constraint)
                {
                    continue;
                }

                if (captured)
                {
                    constraint.Activate();
                }
                else
                {
                    constraint.Deactivate();
                }
            }
        });
    }

    private int _screenWindowId;

    public ToplevelWindow? ScreenWindow =>
        _screenWindowId != 0 && _windows.TryGetValue(_screenWindowId, out var window) ? window : null;

    private void RunOnUi(Action action) => Dispatcher.UIThread.Post(action);

    private ToplevelWindow? WindowOf(Entry entry) =>
        _windows.TryGetValue(entry.Id, out var window) ? window : null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseCursorSurface();
        _host.Shell.NewToplevel -= OnNewToplevel;
        if (_layerShell is not null)
        {
            _layerShell.NewSurface -= OnNewLayerSurface;
        }

        if (_decorations is not null)
        {
            _decorations.ModeChanged -= OnDecorationModeChanged;
        }
    }
}
