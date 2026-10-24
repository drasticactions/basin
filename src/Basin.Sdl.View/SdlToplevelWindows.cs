using Basin.Capabilities;
using Basin.Desktop;
using Basin.Hosted;
using Basin.Scene;
using Basin.Shell.Xdg;
using SDL3;
using static Basin.Sdl.SdlViewLog;

namespace Basin.Sdl;

/// <summary>
/// Gives every <c>xdg_toplevel</c> and <c>xdg_popup</c> an SDL window of its own. Each surface sits in a scene cell, so no two can overlap.
/// Each window is an advertised output that its surface enters.
/// </summary>
public sealed class SdlToplevelWindows : IDisposable
{
    /// <summary>
    /// The vertical distance in logical units between two scene cells.
    /// </summary>
    public const int CellStride = 16384;

    private readonly SdlLoop _loop;
    private readonly BasinCompositorHost _host;
    private readonly ISdlRenderDevice _device;
    private readonly SdlFrameLoop _frames;
    private readonly Dictionary<XdgToplevelWindow, Entry> _entries = [];
    private readonly Dictionary<XdgPopupWindow, Entry> _popups = [];
    private readonly Action<FrameTick> _followDrag;
    private readonly SdlWaylandMoves? _waylandMoves;
    private Drag? _drag;
    private bool _reportedNoMove;

    private sealed record Drag(Entry Entry, ResizeEdges Edges, float MouseX, float MouseY, int X, int Y, int Width, int Height);
    private readonly Stack<int> _freeCells = new();
    private readonly XdgDecorationManager? _decorations;
    private readonly KdeServerDecorationManager? _kdeDecorations;
    private readonly XdgToplevelSource? _toplevelSource;
    private readonly XdgToplevelRequestHandler? _requestHandler;
    private int _nextCell;
    private bool _disposed;

    /// <summary>
    /// The state of one toplevel or popup and its window.
    /// </summary>
    public sealed class Entry
    {
        internal Entry(XdgToplevelWindow toplevel, SceneSurface scene, int cell)
        {
            Toplevel = toplevel;
            Surface = toplevel.Surface;
            Scene = scene;
            Cell = cell;
            Key = $"toplevel-{cell}";
        }

        internal Entry(XdgPopupWindow popup, SceneSurface scene, int cell, Entry owner)
        {
            Popup = popup;
            Toplevel = owner.Toplevel;
            Surface = popup.Surface;
            Scene = scene;
            Cell = cell;
            Key = $"popup-{cell}";
            Owner = owner;
        }

        /// <summary>
        /// Gets the toplevel, or the toplevel that owns the popup chain.
        /// </summary>
        public XdgToplevelWindow Toplevel { get; }

        /// <summary>
        /// Gets the popup. It is <see langword="null"/> for a toplevel entry.
        /// </summary>
        public XdgPopupWindow? Popup { get; }

        /// <summary>
        /// Gets the entry of the parent of a popup. It is <see langword="null"/> for a toplevel entry.
        /// </summary>
        public Entry? Owner { get; }

        /// <summary>
        /// Gets the surface of the toplevel or the popup.
        /// </summary>
        public Surface Surface { get; }

        /// <summary>
        /// Gets the drop target of a toplevel window. It is <see langword="null"/> for a popup and while no window exists.
        /// </summary>
        public SdlDrop? Drop { get; internal set; }

        /// <summary>
        /// Gets the scene node of the surface in its cell.
        /// </summary>
        public SceneSurface Scene { get; }

        /// <summary>
        /// Gets the cell index. A cell is reused after its entry goes away.
        /// </summary>
        public int Cell { get; }

        /// <summary>
        /// Gets the name of the output of the entry, for example "toplevel-0" or "popup-3".
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets the SDL window of the entry. It is <see langword="null"/> before the first map and after the close.
        /// </summary>
        public SdlOutputWindow? Window { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the window is an advertised output and the surface entered it.
        /// </summary>
        public bool Advertised { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the client draws its own frame, so the window is borderless and transparent.
        /// </summary>
        public bool ClientDecorated { get; internal set; }

        internal (int Width, int Height) FollowedSize;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlToplevelWindows"/> class. It follows the new toplevels and popups of the host shell.
    /// It turns <see cref="SdlFrameLoop.SuspendWhenHidden"/> off, because a hidden window is ordinary in this mode.
    /// </summary>
    /// <param name="loop">The SDL loop that delivers the window events.</param>
    /// <param name="host">The host whose shell makes the toplevels.</param>
    /// <param name="device">The render device that every window uses.</param>
    /// <param name="frames">The frame loop that renders the windows.</param>
    public SdlToplevelWindows(SdlLoop loop, BasinCompositorHost host, ISdlRenderDevice device, SdlFrameLoop frames)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(frames);
        _loop = loop;
        _host = host;
        _device = device;
        _frames = frames;
        frames.SuspendWhenHidden = false;
        _decorations = host.Services.Find<XdgDecorationManager>();
        if (_decorations is not null)
        {
            _decorations.DefaultMode = DecorationMode.ServerSide;
            _decorations.ChooseMode = (_, preference) => preference ?? DecorationMode.ServerSide;
            _decorations.ModeChanged += (toplevel, _) => Redecorate(toplevel.Surface);
        }

        _kdeDecorations = host.Services.Find<KdeServerDecorationManager>();
        if (_kdeDecorations is not null)
        {
            _kdeDecorations.ModeRequested += (surface, _) => Redecorate(surface);
        }

        _toplevelSource = host.Services.Find<XdgToplevelSource>();
        if (_toplevelSource is { RequestHandler: null })
        {
            _requestHandler = AnswerRequest;
            _toplevelSource.RequestHandler = _requestHandler;
        }

        host.Shell.NewToplevel += OnNewToplevel;
        host.Shell.NewPopup += OnNewPopup;
        _waylandMoves = SdlWaylandMoves.TryCreate();
        _followDrag = _ => FollowDrag();
        host.Session.BeforeDispatch += _followDrag;
    }

    /// <summary>
    /// Gets the entries of the popups that exist now.
    /// </summary>
    public IReadOnlyCollection<Entry> Popups => _popups.Values;

    /// <summary>
    /// Gets a value indicating whether the SDL driver can place a window. It is <see langword="false"/> on the wayland and offscreen drivers.
    /// </summary>
    public bool CanMoveWindows { get; } = SDL.GetCurrentVideoDriver() is not ("wayland" or "offscreen");

    /// <summary>
    /// Gets the entries of the toplevels that exist now.
    /// </summary>
    public IReadOnlyCollection<Entry> Entries => _entries.Values;

    /// <summary>
    /// Occurs when a toplevel gets its SDL window at its first map. Popups do not raise it.
    /// </summary>
    public event Action<Entry>? WindowOpened;

    /// <summary>
    /// Occurs when the window of a toplevel or a popup closes, after the window is disposed.
    /// </summary>
    public event Action<Entry>? WindowClosed;

    /// <summary>
    /// Finds the entry of a toplevel.
    /// </summary>
    /// <param name="toplevel">The toplevel.</param>
    /// <returns>The entry, or <see langword="null"/> when the toplevel is unknown.</returns>
    public Entry? EntryOf(XdgToplevelWindow toplevel) => _entries.GetValueOrDefault(toplevel);

    /// <summary>
    /// Finds the toplevel entry that a window shows. Popup windows are not searched.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The entry, or <see langword="null"/> when no toplevel uses the window.</returns>
    public Entry? EntryOf(SdlOutputWindow window)
    {
        foreach (var entry in _entries.Values)
        {
            if (ReferenceEquals(entry.Window, window))
            {
                return entry;
            }
        }

        return null;
    }

    private void OnNewToplevel(XdgToplevelWindow toplevel)
    {
        var cell = _freeCells.Count > 0 ? _freeCells.Pop() : _nextCell++;
        var entry = new Entry(toplevel, new SceneSurface(_host.Scene.Root, toplevel.Surface), cell);
        entry.Scene.Tree.SetPosition(0, cell * CellStride);
        _entries[toplevel] = entry;
        toplevel.Xdg.Mapped += () => Guard(() => OnMapped(entry), entry);
        toplevel.Xdg.Unmapped += () => OnUnmapped(entry);
        toplevel.Xdg.Committed += () => Follow(entry);
        toplevel.TitleChanged += () => ApplyTitle(entry);
        toplevel.AppIdChanged += () => ApplyTitle(entry);
        toplevel.Destroyed += () => OnDestroyed(entry);
        toplevel.MaximizeRequested += on => WithWindow(entry, handle => _ = on ? SDL.MaximizeWindow(handle) : SDL.RestoreWindow(handle));
        toplevel.FullscreenRequested += on => WithWindow(entry, handle => _ = SDL.SetWindowFullscreen(handle, on));
        toplevel.MinimizeRequested += () => WithWindow(entry, handle => _ = SDL.MinimizeWindow(handle));
        toplevel.ShowWindowMenuRequested += (x, y) => WithWindow(entry, handle =>
        {
            var (pointX, pointY) = PointsOf(entry, x, y);
            _ = SDL.ShowWindowSystemMenu(handle, pointX, pointY);
        });
        toplevel.MoveRequested += _ => BeginDrag(entry, ResizeEdges.None);
        toplevel.ResizeRequested += (_, edges) => BeginDrag(entry, edges);
    }

    private static void Guard(Action map, Entry entry)
    {
        try
        {
            map();
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Error(error, $"window: no SDL window for {entry.Key}, surface closed");
            if (entry.Popup is { } popup)
            {
                popup.Dismiss();
            }
            else
            {
                entry.Toplevel.Close();
            }
        }
    }

    private static void WithWindow(Entry entry, Action<nint> action)
    {
        if (entry.Window is { } window)
        {
            action(window.Window.Handle);
        }
    }

    private static (int X, int Y) PointsOf(Entry entry, int logicalX, int logicalY)
    {
        if (entry.Window is not { } window)
        {
            return (logicalX, logicalY);
        }

        var points = window.View.Target.Scale / window.Window.PixelDensity;
        var frame = FrameOf(entry);
        return ((int)Math.Round((logicalX - frame.X) * points), (int)Math.Round((logicalY - frame.Y) * points));
    }

    private bool ServerDecorated(XdgToplevelWindow toplevel) =>
        _kdeDecorations?.ModeOf(toplevel.Surface) == KdeServerDecorationManager.DecorationMode.Server ||
        _decorations?.ModeOf(toplevel) == DecorationMode.ServerSide;

    private bool? AnswerRequest(XdgToplevelWindow toplevel, in ToplevelRequest request)
    {
        if (!_entries.TryGetValue(toplevel, out var entry) || entry.Window is not { } window)
        {
            return null;
        }

        var points = window.View.Target.Scale / window.Window.PixelDensity;
        switch (request.Kind)
        {
            case ToplevelRequestKind.Resize:
                var frame = FrameOf(entry);
                var geometry = toplevel.Xdg.PresentedGeometry;
                var width = request.Geometry.Width + frame.Width - geometry.Width;
                var height = request.Geometry.Height + frame.Height - geometry.Height;
                if (request.Geometry.Width <= 0 || request.Geometry.Height <= 0)
                {
                    return false;
                }

                return SDL.SetWindowSize(
                    window.Window.Handle,
                    Math.Max(1, (int)Math.Round(width * points)),
                    Math.Max(1, (int)Math.Round(height * points)));
            case ToplevelRequestKind.Move:
                return CanMoveWindows && SDL.SetWindowPosition(
                    window.Window.Handle,
                    (int)Math.Round(request.Geometry.X * points),
                    (int)Math.Round(request.Geometry.Y * points));
            default:
                return null;
        }
    }

    private static Box FrameOf(Entry entry)
    {
        var geometry = entry.Toplevel.Xdg.PresentedGeometry;
        if (!entry.ClientDecorated)
        {
            return geometry;
        }

        var surface = entry.Toplevel.Surface.Current;
        var left = Math.Min(0, geometry.X);
        var top = Math.Min(0, geometry.Y);
        var right = Math.Max(surface.Width, geometry.X + geometry.Width);
        var bottom = Math.Max(surface.Height, geometry.Y + geometry.Height);
        return new Box(left, top, right - left, bottom - top);
    }

    private void Redecorate(Surface surface)
    {
        foreach (var entry in _entries.Values)
        {
            if (!ReferenceEquals(entry.Surface, surface) || entry.Window is not { } window)
            {
                continue;
            }

            var clientDecorated = !ServerDecorated(entry.Toplevel);
            if (clientDecorated != entry.ClientDecorated)
            {
                entry.ClientDecorated = clientDecorated;
                _ = SDL.SetWindowBordered(window.Window.Handle, !clientDecorated);
                entry.FollowedSize = default;
                Follow(entry);
            }
        }
    }

    private void OnStateChanged(Entry entry, SDL.EventType type)
    {
        var toplevel = entry.Toplevel;
        switch (type)
        {
            case SDL.EventType.WindowMaximized:
                toplevel.SetMaximized(true);
                break;
            case SDL.EventType.WindowRestored:
                toplevel.SetMaximized(false);
                toplevel.SetFullscreen(false);
                break;
            case SDL.EventType.WindowEnterFullscreen:
                toplevel.SetFullscreen(true);
                break;
            case SDL.EventType.WindowLeaveFullscreen:
                toplevel.SetFullscreen(false);
                break;
            case SDL.EventType.WindowMinimized:
                return;
        }

        toplevel.RequestConfigure();
    }

    private void BeginDrag(Entry entry, ResizeEdges edges)
    {
        if (entry.Window is not { } window)
        {
            return;
        }

        if (_waylandMoves is { } moves && entry.Popup is null &&
            moves.TryBegin(window.Window.Handle, (uint)edges))
        {
            return;
        }

        if (!CanMoveWindows)
        {
            if (!_reportedNoMove)
            {
                _reportedNoMove = true;
                Log.Info($"move: SDL {SDL.GetCurrentVideoDriver()} driver cannot start a client move or resize, host frame or move binding does it");
            }

            return;
        }

        var buttons = SDL.GetGlobalMouseState(out var mouseX, out var mouseY);
        if ((buttons & SDL.MouseButtonFlags.Left) == 0)
        {
            return;
        }

        _ = SDL.GetWindowPosition(window.Window.Handle, out var x, out var y);
        _ = SDL.GetWindowSize(window.Window.Handle, out var width, out var height);
        _drag = new Drag(entry, edges, mouseX, mouseY, x, y, width, height);
        _ = SDL.CaptureMouse(true);
        _loop.MarkActivity();
    }

    private void FollowDrag()
    {
        _waylandMoves?.Dispatch();
        if (_drag is not { } drag || drag.Entry.Window is not { } window)
        {
            return;
        }

        var buttons = SDL.GetGlobalMouseState(out var mouseX, out var mouseY);
        if ((buttons & SDL.MouseButtonFlags.Left) == 0)
        {
            _drag = null;
            _ = SDL.CaptureMouse(false);
            return;
        }

        var dx = (int)Math.Round(mouseX - drag.MouseX);
        var dy = (int)Math.Round(mouseY - drag.MouseY);
        var handle = window.Window.Handle;
        if (drag.Edges == ResizeEdges.None)
        {
            _ = SDL.SetWindowPosition(handle, drag.X + dx, drag.Y + dy);
        }
        else
        {
            var x = drag.X;
            var y = drag.Y;
            var width = drag.Width;
            var height = drag.Height;
            if ((drag.Edges & ResizeEdges.Left) != 0)
            {
                x += dx;
                width -= dx;
            }
            else if ((drag.Edges & ResizeEdges.Right) != 0)
            {
                width += dx;
            }

            if ((drag.Edges & ResizeEdges.Top) != 0)
            {
                y += dy;
                height -= dy;
            }
            else if ((drag.Edges & ResizeEdges.Bottom) != 0)
            {
                height += dy;
            }

            _ = SDL.SetWindowPosition(handle, x, y);
            _ = SDL.SetWindowSize(handle, Math.Max(32, width), Math.Max(32, height));
        }

        _loop.WakeAfter(8);
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (OwnerOf(popup.Parent) is not { } owner)
        {
            return;
        }

        var cell = _freeCells.Count > 0 ? _freeCells.Pop() : _nextCell++;
        var entry = new Entry(popup, new SceneSurface(_host.Scene.Root, popup.Surface), cell, owner);
        entry.Scene.Tree.SetPosition(0, cell * CellStride);
        _popups[popup] = entry;
        popup.Unconstrain(new Box(-CellStride, -CellStride, CellStride * 2, CellStride * 2));
        popup.Xdg.Mapped += () => Guard(() => OnPopupMapped(entry), entry);
        popup.Xdg.Unmapped += () => OnUnmapped(entry);
        popup.Xdg.Committed += () => FollowPopup(entry);
        popup.GeometryChanged += () => PlacePopup(entry);
        popup.Repositioned += () => PlacePopup(entry);
        popup.Destroyed += () => OnPopupDestroyed(entry);
    }

    private Entry? OwnerOf(XdgSurfaceState? parent)
    {
        if (parent is null)
        {
            return null;
        }

        foreach (var entry in _entries.Values)
        {
            if (ReferenceEquals(entry.Toplevel.Xdg, parent))
            {
                return entry;
            }
        }

        foreach (var entry in _popups.Values)
        {
            if (ReferenceEquals(entry.Popup!.Xdg, parent))
            {
                return entry;
            }
        }

        return null;
    }

    private (int X, int Y) PopupOffset(Entry entry)
    {
        var owner = entry.Owner!;
        var window = owner.Window!;
        var points = window.View.Target.Scale / window.Window.PixelDensity;
        var geometry = entry.Popup!.Geometry;
        var (marginX, marginY) = (0, 0);
        if (owner.Popup is null)
        {
            var frame = FrameOf(owner);
            var ownerGeometry = owner.Toplevel.Xdg.PresentedGeometry;
            (marginX, marginY) = (ownerGeometry.X - frame.X, ownerGeometry.Y - frame.Y);
        }

        return ((int)Math.Round((geometry.X + marginX) * points), (int)Math.Round((geometry.Y + marginY) * points));
    }

    private void OnPopupMapped(Entry entry)
    {
        if (_disposed || entry.Owner?.Window is not { } parent)
        {
            return;
        }

        if (entry.Window is { } existing)
        {
            _ = SDL.ShowWindow(existing.Window.Handle);
            PlacePopup(entry);
            return;
        }

        var popup = entry.Popup!;
        var (offsetX, offsetY) = PopupOffset(entry);
        var points = parent.View.Target.Scale / parent.Window.PixelDensity;
        var window = new SdlOutputWindow(_loop, _host, _device, new SdlOutputWindowOptions
        {
            OutputName = entry.Key,
            Width = Math.Max(1, (int)Math.Round(popup.Geometry.Width * points)),
            Height = Math.Max(1, (int)Math.Round(popup.Geometry.Height * points)),
            PopupParent = parent.Window,
            PopupX = offsetX,
            PopupY = offsetY,
            Tooltip = !popup.HasGrab,
            Transparent = true,
            Resizable = false,
            Scale = parent.View.Target.Scale,
        });
        entry.Window = window;
        window.InputSink = input => Route(entry, input);
        FollowPopup(entry);
        Advertise(entry);
        _frames.Add(window);
        window.Window.Show();
    }

    private void PlacePopup(Entry entry)
    {
        if (entry.Window is not { } window)
        {
            return;
        }

        var (offsetX, offsetY) = PopupOffset(entry);
        _ = SDL.SetWindowPosition(window.Window.Handle, offsetX, offsetY);
        FollowPopup(entry);
    }

    private void FollowPopup(Entry entry)
    {
        if (entry.Window is not { } window)
        {
            return;
        }

        var geometry = entry.Popup!.Xdg.EffectiveGeometry;
        window.View.SceneOutput.Position = new Point(geometry.X, (entry.Cell * CellStride) + geometry.Y);
        var size = entry.Popup.Geometry;
        var points = window.View.Target.Scale / window.Window.PixelDensity;
        var width = Math.Max(1, (int)Math.Round(size.Width * points));
        var height = Math.Max(1, (int)Math.Round(size.Height * points));
        _ = SDL.GetWindowSize(window.Window.Handle, out var currentWidth, out var currentHeight);
        if (currentWidth != width || currentHeight != height)
        {
            _ = SDL.SetWindowSize(window.Window.Handle, width, height);
        }
    }

    private void OnPopupDestroyed(Entry entry)
    {
        if (!_popups.Remove(entry.Popup!))
        {
            return;
        }

        Close(entry);
        _freeCells.Push(entry.Cell);
    }

    private static string TitleOf(XdgToplevelWindow toplevel) =>
        toplevel.Title.Length > 0 ? toplevel.Title : toplevel.AppId.Length > 0 ? toplevel.AppId : "basin";

    private void ApplyTitle(Entry entry)
    {
        if (entry.Window is { } window)
        {
            window.Window.Title = TitleOf(entry.Toplevel);
        }
    }

    private void OnMapped(Entry entry)
    {
        if (_disposed)
        {
            return;
        }

        if (entry.Window is { } existing)
        {
            _ = SDL.ShowWindow(existing.Window.Handle);
            Follow(entry);
            return;
        }

        entry.ClientDecorated = !ServerDecorated(entry.Toplevel);
        var geometry = FrameOf(entry);
        var window = new SdlOutputWindow(_loop, _host, _device, new SdlOutputWindowOptions
        {
            Title = TitleOf(entry.Toplevel),
            Width = Math.Max(1, geometry.Width),
            Height = Math.Max(1, geometry.Height),
            OutputName = entry.Key,
            Borderless = entry.ClientDecorated,
            Transparent = entry.ClientDecorated,
        });
        entry.Window = window;
        window.InputSink = input => Route(entry, input);
        window.ResizeHandler = (width, height, scale) => Resized(entry, width, height, scale);
        window.CloseRequested += _ => entry.Toplevel.Close();
        window.FocusGained += _ => Activate(entry, true);
        window.StateChanged += (_, type) => OnStateChanged(entry, type);
        entry.Drop = new SdlDrop(_host, _loop, window);
        Follow(entry);
        Advertise(entry);
        _frames.Add(window);
        window.Window.Show();
        Log.Debug($"toplevel window {entry.Key} for '{entry.Toplevel.Title}' at {geometry.Width}x{geometry.Height}");
        WindowOpened?.Invoke(entry);
    }

    private void Advertise(Entry entry)
    {
        if (entry.Window is not { } window)
        {
            return;
        }

        var view = window.View;
        var mode = view.Output.CurrentMode;
        var scale = view.Output.Scale;
        _host.Screens.Advertise(view.Output, new HostScreenInfo(
            entry.Key, entry.Key, 0, (int)Math.Round(entry.Cell * CellStride * scale), mode.Width, mode.Height, scale, entry.Cell == 0));
        if (!entry.Advertised)
        {
            entry.Advertised = true;
            _host.Screens.EnterScreen(entry.Surface, entry.Key);
        }
    }

    private void Follow(Entry entry)
    {
        if (entry.Window is not { } window)
        {
            return;
        }

        var geometry = FrameOf(entry);
        var cellY = entry.Cell * CellStride;
        window.View.SceneOutput.Position = new Point(geometry.X, cellY + geometry.Y);
        _toplevelSource?.SetGeometry(entry.Toplevel, geometry.Translated(0, cellY), entry.Toplevel.Xdg.PresentedGeometry.Translated(0, cellY));
        if (geometry.Width <= 0 || geometry.Height <= 0)
        {
            return;
        }

        var target = window.View.Target;
        var logicalWidth = (int)Math.Round(target.Width / target.Scale);
        var logicalHeight = (int)Math.Round(target.Height / target.Scale);
        if ((geometry.Width != logicalWidth || geometry.Height != logicalHeight) &&
            (geometry.Width, geometry.Height) != entry.FollowedSize)
        {
            entry.FollowedSize = (geometry.Width, geometry.Height);
            var density = window.Window.PixelDensity;
            var points = target.Scale / density;
            _ = SDL.SetWindowSize(
                window.Window.Handle,
                Math.Max(1, (int)Math.Round(geometry.Width * points)),
                Math.Max(1, (int)Math.Round(geometry.Height * points)));
        }
    }

    private void Resized(Entry entry, int width, int height, double scale)
    {
        if (entry.Window is not { } window)
        {
            return;
        }

        window.View.Reconfigure(width, height, scale, OutputTransform.Normal);
        Advertise(entry);
        var frame = FrameOf(entry);
        var geometry = entry.Toplevel.Xdg.PresentedGeometry;
        var logicalWidth = Math.Max(1, (int)Math.Round(width / scale) - (frame.Width - geometry.Width));
        var logicalHeight = Math.Max(1, (int)Math.Round(height / scale) - (frame.Height - geometry.Height));
        var (configuredWidth, configuredHeight) = entry.Toplevel.ConfiguredSize;
        if (configuredWidth != logicalWidth || configuredHeight != logicalHeight)
        {
            entry.Toplevel.SetSize(logicalWidth, logicalHeight);
            _ = entry.Toplevel.SendConfigure();
        }
    }

    private void Activate(Entry entry, bool activated)
    {
        entry.Toplevel.SetActivated(activated);
        _ = entry.Toplevel.SendConfigure();
    }

    private void Route(Entry entry, BasinViewInput input)
    {
        if (entry.Window is not { } window)
        {
            return;
        }

        var origin = window.View.SceneOutput.Position;
        var layoutX = origin.X + input.X;
        var layoutY = origin.Y + input.Y;
        var pointer = _host.Seat.Pointer;
        switch (input.Kind)
        {
            case BasinViewInputKind.PointerEnter:
            case BasinViewInputKind.PointerMotion:
                var hit = _host.Scene.SurfaceAt(layoutX, layoutY);
                pointer.NotifyMotionAt(input.TimeMs, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, layoutX, layoutY);
                break;
            case BasinViewInputKind.PointerLeave:
                pointer.NotifyClearFocus();
                break;
            case BasinViewInputKind.PointerButton:
                pointer.NotifyButton(input.TimeMs, input.Code, input.Pressed);
                break;
            case BasinViewInputKind.PointerAxis:
                if (input.DeltaY != 0)
                {
                    pointer.NotifyAxis(input.TimeMs, new PointerAxis(
                        Wayland.WlPointer.Axis.VerticalScroll, -input.DeltaY * 10, (int)(-input.DeltaY * 120)));
                }

                if (input.DeltaX != 0)
                {
                    pointer.NotifyAxis(input.TimeMs, new PointerAxis(
                        Wayland.WlPointer.Axis.HorizontalScroll, -input.DeltaX * 10, (int)(-input.DeltaX * 120)));
                }

                break;
            case BasinViewInputKind.Key:
                _host.Seat.Keyboard.NotifyKey(input.TimeMs, input.Code, input.Pressed);
                break;
            case BasinViewInputKind.FocusIn:
                if (entry.Popup is null)
                {
                    _host.Seat.Keyboard.NotifyEnter(entry.Toplevel.Surface);
                }

                break;
            case BasinViewInputKind.FocusOut:
                if (entry.Popup is null)
                {
                    if (ReferenceEquals(_host.Seat.Keyboard.Focus, entry.Toplevel.Surface))
                    {
                        _host.Seat.Keyboard.NotifyClearFocus();
                    }

                    Activate(entry, false);
                }

                break;
            case BasinViewInputKind.TouchDown:
                if (_host.Scene.SurfaceAt(layoutX, layoutY) is { Surface: { } touched } at)
                {
                    _host.Seat.Touch.NotifyDown(touched, input.TimeMs, input.TouchId, at.X, at.Y);
                    _host.Seat.Touch.NotifyFrame();
                }

                break;
            case BasinViewInputKind.TouchMotion:
                if (_host.Scene.SurfaceAt(layoutX, layoutY) is { } moved)
                {
                    _host.Seat.Touch.NotifyMotion(input.TimeMs, input.TouchId, moved.X, moved.Y);
                    _host.Seat.Touch.NotifyFrame();
                }

                break;
            case BasinViewInputKind.TouchUp:
                _host.Seat.Touch.NotifyUp(input.TimeMs, input.TouchId);
                _host.Seat.Touch.NotifyFrame();
                break;
            case BasinViewInputKind.TouchCancel:
                _host.Seat.Touch.NotifyCancel();
                break;
        }
    }

    private void OnUnmapped(Entry entry)
    {
        if (entry.Window is { } window)
        {
            _ = SDL.HideWindow(window.Window.Handle);
        }
    }

    private void OnDestroyed(Entry entry)
    {
        if (!_entries.Remove(entry.Toplevel))
        {
            return;
        }

        Close(entry);
        _freeCells.Push(entry.Cell);
    }

    private void Close(Entry entry)
    {
        entry.Drop?.Dispose();
        entry.Drop = null;
        if (_drag?.Entry == entry)
        {
            _drag = null;
            _ = SDL.CaptureMouse(false);
        }

        if (entry.Window is { } window)
        {
            _frames.Remove(window);
            if (entry.Advertised)
            {
                _host.Screens.Withdraw(entry.Key);
            }

            window.Dispose();
            entry.Window = null;
            WindowClosed?.Invoke(entry);
        }

        entry.Scene.Destroy();
    }

    /// <summary>
    /// Stops following the shell and closes every popup and toplevel window. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.Shell.NewToplevel -= OnNewToplevel;
        _host.Shell.NewPopup -= OnNewPopup;
        _host.Session.BeforeDispatch -= _followDrag;
        if (_toplevelSource is not null && _toplevelSource.RequestHandler == _requestHandler)
        {
            _toplevelSource.RequestHandler = null;
        }

        _waylandMoves?.Dispose();
        foreach (var entry in _popups.Values.ToArray())
        {
            Close(entry);
        }

        foreach (var entry in _entries.Values.ToArray())
        {
            Close(entry);
        }

        _popups.Clear();
        _entries.Clear();
    }
}
