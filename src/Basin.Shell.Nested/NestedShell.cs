using System.Globalization;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Frames.Metacity;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;
using Basin.UI.Skia;
using Wayland.Server;
using Basin.Hosted;
using Basin.Seat;

namespace Basin.Shell.Nested;

/// <summary>
/// The window model of a compositor that lives inside one host view, with one output.
/// It places, focuses, stacks, frames and switches windows, and routes the input of the host view.
/// </summary>
public sealed partial class NestedShell : IDisposable
{
    private readonly BasinCompositorHost _host;
    private readonly Action<Action> _post;
    private readonly TouchPointerDriver _touchPointer;
    private readonly TouchGestureGrab _gestureGrab;
    private readonly IReadOnlyList<Func<string, MetacityTheme?>> _themeFallbacks;
    private Func<WlClient, string?>? _windowSuffix;
    private readonly List<ManagedWindow> _windows = [];
    private readonly Dictionary<XdgToplevelWindow, ManagedWindow> _byToplevel = [];
    private readonly FocusHistory<ManagedWindow> _history = new();
    private readonly XdgToplevelSource? _toplevels;
    private readonly XdgDecorationManager? _decorations;
    private readonly KdeServerDecorationManager? _kdeDecorations;
    private readonly Dictionary<Surface, bool> _decorationPreference = [];
    private readonly PopupPlacer _popups;
    private readonly UISurfaceIndex _uiIndex = new();
    private readonly UISurfaceRouter _router;
    private ShellSettings _settings;
    private PanelLayout _panel;
    private KeyTable _keys;
    private MetacityFrames _frames;
    private readonly SceneRect _background;
    private Workspaces _workspaces;
    private ManagedWindow? _focused;
    private long _nextId;
    private bool _showingDesktop;
    private bool _disposed;

    private readonly NestedSyntheticInput _syntheticInput;

    /// <summary>
    /// Initializes a new instance of the <see cref="NestedShell"/> class. It connects to the shell, seat, decoration and toplevel services of <paramref name="host"/>.
    /// It installs its synthetic input and its toplevel request handler only where none is set.
    /// </summary>
    /// <param name="host">The compositor host.</param>
    /// <param name="view">The output of the host view. It is the only output of the shell.</param>
    /// <param name="settings">The initial settings.</param>
    /// <param name="panel">The panel strip geometry.</param>
    /// <param name="keys">The key bindings.</param>
    /// <param name="post">Runs a callback on the compositor loop. It must accept calls from any thread.</param>
    /// <param name="themeFallbacks">The theme sources to try when no installed theme has the configured name, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="themeFallbacks"/> is <see langword="null"/>.</exception>
    /// <exception cref="MetacityThemeException">No theme has the configured name.</exception>
    public NestedShell(
        BasinCompositorHost host,
        BasinViewOutput view,
        ShellSettings settings,
        PanelLayout panel,
        KeyTable keys,
        Action<Action> post,
        IReadOnlyList<Func<string, MetacityTheme?>>? themeFallbacks = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(post);
        _host = host;
        View = view;
        _settings = settings;
        _panel = panel;
        _keys = keys;
        _post = post;
        _themeFallbacks = themeFallbacks ?? [];
        UIHost = new SkiaUIHost();
        _frames = MetacityFrames.Load(settings, _themeFallbacks);
        Layers = new ShellLayers(host.Scene.Root);
        _syntheticInput = new NestedSyntheticInput(this);
        host.Seat.SyntheticInput ??= _syntheticInput;
        _workspaces = new Workspaces(settings.Workspaces, settings.WorkspaceRows, settings.WorkspaceNames);
        _router = new UISurfaceRouter(host.Scene, _uiIndex);
        _popups = new PopupPlacer(host.Layout);
        _toplevels = host.Services.Find<XdgToplevelSource>();
        _decorations = host.Services.Find<XdgDecorationManager>();
        if (_decorations is not null)
        {
            _decorations.DefaultMode = DecorationMode.ServerSide;
            _decorations.ChooseMode = (_, preference) => preference ?? DecorationMode.ServerSide;
            _decorations.ModeChanged += (toplevel, mode) =>
                RecordDecorationPreference(toplevel.Surface, mode == DecorationMode.ServerSide);
        }

        _kdeDecorations = host.Services.Find<KdeServerDecorationManager>();
        if (_kdeDecorations is not null)
        {
            _kdeDecorations.ModeRequested += (surface, mode) =>
                RecordDecorationPreference(surface, mode == KdeServerDecorationManager.DecorationMode.Server);
        }

        _model = host.Services.Find<IToplevelModel>();
        _adopted = new NestedToplevelSource(this);
        if (_model is AggregateToplevelModel aggregate)
        {
            aggregate.Add(_adopted);
        }

        if (_toplevels is not null)
        {
            if (_toplevels.RequestHandler is null)
            {
                _xdgHandler = AnswerXdgRequest;
                _toplevels.RequestHandler = _xdgHandler;
            }

            _toplevels.ActivateRequested += toplevel => Activate(toplevel);
            _toplevels.MinimizeRequested += (toplevel, minimized) =>
            {
                if (_byToplevel.TryGetValue(toplevel, out var window))
                {
                    SetMinimized(window, minimized);
                }
            };
        }

        var mode = view.Output.CurrentMode;
        Scale = view.Output.Scale;
        var (oriented, orientedHeight) = OrientedSize(mode.Width, mode.Height, view.Output.Transform);
        Output = new Box(0, 0, Logical(oriented, Scale), Logical(orientedHeight, Scale));
        WorkArea = WorkAreaFor(Output, _panel);
        _background = new SceneRect(Layers.Background, Output.Width, Output.Height, BackgroundColor(settings));
        host.Screens.Advertise(view.Output, ScreenInfo(oriented, orientedHeight, Scale, view.Output.Transform));
        _touchPointer = new TouchPointerDriver(host.Seat.Touch, new TouchPointerTarget(this)) { ClaimWithoutSurface = true };
        _gestureGrab = new TouchGestureGrab(this);
        host.Seat.SetCapability(SeatCapability.Touch, true);
        AttachPointerConstraints();
        host.Shell.NewToplevel += OnNewToplevel;
        host.Shell.NewPopup += OnNewPopup;
        host.Seat.Pointer.CursorRequested += OnCursorRequested;
        if (host.Services.Find<CursorShapeManager>() is { } shapes)
        {
            shapes.ShapeRequested += OnShapeRequested;
        }

        if (host.Services.Find<XdgToplevelIconManager>() is { } icons)
        {
            icons.IconChanged += (toplevel, name) =>
            {
                if (_byToplevel.TryGetValue(toplevel, out var window))
                {
                    SetClientIcon(window, name);
                }
            };
        }
    }

    /// <summary>
    /// Gets the output of the host view.
    /// </summary>
    public BasinViewOutput View { get; }

    /// <summary>
    /// Gets the compositor host.
    /// </summary>
    public BasinCompositorHost Host => _host;

    /// <summary>
    /// Gets the UI host that draws the frames. The shell owns it and disposes it.
    /// </summary>
    public SkiaUIHost UIHost { get; }

    /// <summary>
    /// Gets the scene layers. A consumer puts its own content, for example the panels, into them.
    /// </summary>
    public ShellLayers Layers { get; }

    /// <summary>
    /// Gets the frame theme in use. <see cref="Apply"/> can replace and dispose it.
    /// </summary>
    public MetacityFrames Frames => _frames;

    /// <summary>
    /// Gets the settings that the last <see cref="Apply"/> or the constructor gave.
    /// </summary>
    public ShellSettings Settings => _settings;

    /// <summary>
    /// Gets the panel strip geometry in use.
    /// </summary>
    public PanelLayout Panel => _panel;

    /// <summary>
    /// Gets the key bindings in use.
    /// </summary>
    public KeyTable Keys => _keys;

    /// <summary>
    /// Gets the workspace grid. <see cref="Apply"/> replaces it when the workspace settings change.
    /// </summary>
    public Workspaces Workspaces => _workspaces;

    /// <summary>
    /// Gets the index of consumer UI surfaces that the shell routes input to.
    /// </summary>
    public UISurfaceIndex UISurfaces => _uiIndex;

    /// <summary>
    /// Gets the router that sends pointer and keyboard input to consumer UI surfaces.
    /// </summary>
    public UISurfaceRouter Router => _router;

    /// <summary>
    /// Gets the output scale.
    /// </summary>
    public double Scale { get; private set; }

    /// <summary>
    /// Gets the output box in logical layout coordinates, after the output transform. It starts at 0, 0.
    /// </summary>
    public Box Output { get; private set; }

    /// <summary>
    /// Gets the part of <see cref="Output"/> that the panel strips do not cover.
    /// </summary>
    public Box WorkArea { get; private set; }

    /// <summary>
    /// Gets the background color from the settings.
    /// </summary>
    public RenderColor Background => _background.Color;

    /// <summary>
    /// Gets or sets the delegate that gives a title suffix for each client. A <see langword="null"/> answer appends nothing.
    /// Setting it labels every window again and publishes.
    /// </summary>
    public Func<WlClient, string?>? WindowSuffix
    {
        get => _windowSuffix;
        set
        {
            _windowSuffix = value;
            foreach (var window in _windows)
            {
                window.Suffix = window.Client is { } client ? value?.Invoke(client) : null;
                window.RefreshFrame();
            }

            Publish();
        }
    }

    /// <summary>
    /// Gets the mapped windows in the order they mapped. The list is live.
    /// </summary>
    public IReadOnlyList<ManagedWindow> Windows => _windows;

    /// <summary>
    /// Gets the focused window, or <see langword="null"/>.
    /// </summary>
    public ManagedWindow? Focused => _focused;

    /// <summary>
    /// Gets a value indicating whether the shell hides the windows to show the desktop.
    /// </summary>
    public bool ShowingDesktop => _showingDesktop;

    /// <summary>
    /// Gets a value indicating whether the shell is disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Occurs on each <see cref="Publish"/>, after a change that a panel shows: windows, focus, titles, icons, workspaces or geometry.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Occurs when the pointer needs a shell cursor, over a frame, the desktop or a grab. The argument is an XCursor name, for example <c>left_ptr</c>.
    /// </summary>
    public event Action<string>? CursorChanged;

    /// <summary>
    /// Occurs when the pointer enters a client, and when that client sets its cursor while the pointer is over it.
    /// </summary>
    public event Action<ShellCursor>? ClientCursorChanged;

    /// <summary>
    /// Occurs when a switch key opens the window switcher. The arguments are the window order and the selected index.
    /// </summary>
    public event Action<IReadOnlyList<ManagedWindow>, int>? SwitcherShown;

    /// <summary>
    /// Occurs when a switch key moves the selection of the open switcher. The argument is the selected index.
    /// </summary>
    public event Action<int>? SwitcherMoved;

    /// <summary>
    /// Occurs when the switcher closes, before the shell activates the selected window.
    /// </summary>
    public event Action? SwitcherHidden;

    /// <summary>
    /// Occurs when the <c>panel-main-menu</c> key is pressed.
    /// </summary>
    public event Action? MainMenuRequested;

    /// <summary>
    /// Occurs when the <c>toggle-host-fullscreen</c> key is pressed.
    /// </summary>
    public event Action? HostFullScreenRequested;

    /// <summary>
    /// Gets or sets the delegate that finds the icon of a window from its suffix, its app id and the icon that the client named.
    /// It gives the answer to the callback, which can run on any thread. When it is <see langword="null"/>, the client icon is used.
    /// </summary>
    public Action<string?, string, string?, Action<string?>>? ResolveIcon { get; set; }

    /// <summary>
    /// Records the icon that a client named and finds the icon again.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="icon">The icon name from the client, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public void SetClientIcon(ManagedWindow window, string? icon)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ClientIcon = icon;
        RequestIcon(window);
    }

    private void RequestIcon(ManagedWindow window)
    {
        if (ResolveIcon is not { } resolver)
        {
            window.IconName = window.ClientIcon;
            window.RefreshFrame();
            return;
        }

        var generation = ++window.IconGeneration;
        resolver(window.Suffix, window.Content.AppId, window.ClientIcon, path => _post(() =>
        {
            if (!window.IsMapped || window.IconGeneration != generation || window.IconName == path)
            {
                return;
            }

            window.IconName = path;
            window.RefreshFrame();
            Publish();
        }));
    }

    /// <summary>
    /// Occurs after a window maps, is placed and gets focus or a demand for attention.
    /// </summary>
    public event Action<ManagedWindow>? WindowMapped;

    /// <summary>
    /// Occurs after a window unmaps and focus moves on.
    /// </summary>
    public event Action<ManagedWindow>? WindowUnmapped;

    /// <summary>
    /// The screen key that the shell advertises for its one output.
    /// </summary>
    public const string OutputKey = "shell";

    private static HostScreenInfo ScreenInfo(int width, int height, double scale, OutputTransform transform) =>
        new(OutputKey, OutputKey, 0, 0, width, height, scale, true) { Transform = transform };

    private static (int Width, int Height) OrientedSize(int width, int height, OutputTransform transform) =>
        transform.SwapsAxes() ? (height, width) : (width, height);

    /// <summary>
    /// Gets the output transform in force.
    /// </summary>
    public OutputTransform Transform => View.Output.Transform;

    /// <summary>
    /// Converts a physical extent to logical pixels.
    /// </summary>
    /// <param name="physical">The extent in physical pixels.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The rounded logical extent, at least 1.</returns>
    public static int Logical(int physical, double scale) => Math.Max(1, (int)Math.Round(physical / scale));

    /// <summary>
    /// Calculates the work area: the output minus each panel strip that has items.
    /// </summary>
    /// <param name="output">The output box.</param>
    /// <param name="panel">The panel strip geometry.</param>
    /// <returns>The work area. Its height is at least 1.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="panel"/> is <see langword="null"/>.</exception>
    public static Box WorkAreaFor(Box output, PanelLayout panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var top = panel.Top > 0 ? panel.Size : 0;
        var bottom = panel.Bottom > 0 ? panel.Size : 0;
        return new Box(output.X, output.Y + top, output.Width, Math.Max(1, output.Height - top - bottom));
    }

    /// <summary>
    /// Gets the box of the top panel strip. Its height is zero when the top strip has no items.
    /// </summary>
    public Box TopStrip => new(Output.X, Output.Y, Output.Width, _panel.Top > 0 ? _panel.Size : 0);

    /// <summary>
    /// Gets the box of the bottom panel strip. Its height is zero when the bottom strip has no items.
    /// </summary>
    public Box BottomStrip => new(
        Output.X, Output.Bottom - (_panel.Bottom > 0 ? _panel.Size : 0), Output.Width, _panel.Bottom > 0 ? _panel.Size : 0);

    /// <summary>
    /// Changes the output mode, scale and transform in one commit, then lays out every window in the new logical space.
    /// It does nothing after dispose or when no value changes.
    /// </summary>
    /// <param name="physicalWidth">The mode width in physical pixels. A value below 1 counts as 1.</param>
    /// <param name="physicalHeight">The mode height in physical pixels. A value below 1 counts as 1.</param>
    /// <param name="scale">The output scale. The shell snaps it to a supported value.</param>
    /// <param name="transform">The output transform. A transform that swaps axes swaps the logical width and height.</param>
    public void Resize(int physicalWidth, int physicalHeight, double scale, OutputTransform transform = OutputTransform.Normal)
    {
        if (_disposed)
        {
            return;
        }

        scale = OutputScaling.Snap(scale);
        var scaleChanged = Math.Abs(scale - Scale) > double.Epsilon;
        physicalWidth = Math.Max(1, physicalWidth);
        physicalHeight = Math.Max(1, physicalHeight);
        var mode = View.Output.CurrentMode;
        if (mode.Width == physicalWidth && mode.Height == physicalHeight && !scaleChanged && View.Output.Transform == transform)
        {
            return;
        }

        View.Reconfigure(physicalWidth, physicalHeight, scale, transform);
        var (orientedWidth, orientedHeight) = OrientedSize(physicalWidth, physicalHeight, transform);
        _host.Screens.Advertise(View.Output, ScreenInfo(orientedWidth, orientedHeight, scale, transform));
        Scale = scale;
        Output = new Box(0, 0, Logical(orientedWidth, scale), Logical(orientedHeight, scale));
        WorkArea = WorkAreaFor(Output, _panel);
        _background.Width = Output.Width;
        _background.Height = Output.Height;
        if (scaleChanged)
        {
            foreach (var window in _windows)
            {
                if (window.Content.Surface is { } surface)
                {
                    _host.Screens.EnterScreen(surface, OutputKey);
                }

                window.Content.OutputScaleChanged(scale);
            }
        }

        Relayout();
    }

    /// <summary>
    /// Fits each mapped window to its state again and keeps each floating title bar on the work area.
    /// It raises <see cref="PanelsChanged"/> and publishes.
    /// </summary>
    public void Relayout()
    {
        foreach (var window in _windows)
        {
            if (!window.IsMapped)
            {
                continue;
            }

            if (window.Fullscreen)
            {
                ApplyFullscreenGeometry(window);
            }
            else if (window.Maximized)
            {
                ApplyMaximizedGeometry(window);
            }
            else if (window.Tile != TileEdge.None)
            {
                ApplyTileGeometry(window, window.Tile);
            }
            else
            {
                var frame = Constraints.KeepTitleOnScreen(window.FrameBox, WorkArea, window.TitleHeight);
                if (frame.X != window.FrameBox.X || frame.Y != window.FrameBox.Y)
                {
                    window.MoveFrameTo(frame.X, frame.Y);
                }
            }

            window.RefreshFrame();
        }

        PanelsChanged?.Invoke();
        Publish();
    }

    /// <summary>
    /// Occurs on each <see cref="Relayout"/>, because the panel strips can have moved.
    /// </summary>
    public event Action? PanelsChanged;

    /// <summary>
    /// Applies new settings while the shell runs. A theme that fails to load is logged, and the old frames stay.
    /// Windows on a removed workspace move to the last workspace.
    /// </summary>
    /// <param name="settings">The new settings.</param>
    /// <param name="panel">The new panel strip geometry.</param>
    /// <param name="keys">The new key bindings.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void Apply(ShellSettings settings, PanelLayout panel, KeyTable keys)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(keys);
        _keys = keys;
        if (!_frames.Matches(settings))
        {
            try
            {
                var frames = MetacityFrames.Load(settings, _themeFallbacks);
                _frames.Dispose();
                _frames = frames;
                foreach (var window in _windows)
                {
                    window.RebuildFrame();
                }
            }
            catch (Exception error) when (error is MetacityThemeException or IOException or FormatException)
            {
                NestedLog.Log.Error($"the frame theme was not changed: {error.Message}");
            }
        }

        if (settings.Workspaces != _settings.Workspaces || settings.WorkspaceRows != _settings.WorkspaceRows
            || !settings.WorkspaceNames.SequenceEqual(_settings.WorkspaceNames))
        {
            var replaced = new Workspaces(settings.Workspaces, settings.WorkspaceRows, settings.WorkspaceNames);
            replaced.Switch(Math.Min(_workspaces.Current, replaced.Count - 1));
            _workspaces = replaced;
            foreach (var window in _windows)
            {
                if (window.Workspace >= _workspaces.Count)
                {
                    window.Workspace = _workspaces.Count - 1;
                }
            }

            ApplyWorkspaceVisibility();
        }

        _settings = settings;
        _panel = panel;
        _background.Color = BackgroundColor(settings);
        WorkArea = WorkAreaFor(Output, _panel);
        foreach (var window in _windows)
        {
            window.RefreshFrame();
        }

        Relayout();
    }

    private void OnNewToplevel(XdgToplevelWindow toplevel)
    {
        var client = toplevel.Surface.Resource.Client;
        var content = new XdgContent(
            toplevel, parent => _byToplevel.GetValueOrDefault(parent)?.Content, IsServerDecorated, _toplevels);
        var window = new ManagedWindow(this, content, ++_nextId, client) { Suffix = _windowSuffix?.Invoke(client) };
        _byToplevel[toplevel] = window;
        toplevel.Xdg.Mapped += () => OnMapped(window);
        toplevel.Xdg.Unmapped += () => OnUnmapped(window);
        toplevel.Xdg.Committed += () => OnCommitted(window);
        toplevel.TitleChanged += Publish;
        toplevel.AppIdChanged += Publish;
        toplevel.Destroyed += () =>
        {
            _byToplevel.Remove(toplevel);
            if (window.IsMapped)
            {
                OnUnmapped(window);
            }
        };
        toplevel.MinimizeRequested += () => SetMinimized(window, true);
        toplevel.MoveRequested += serial => BeginMove(window, serial);
        toplevel.ResizeRequested += (serial, edges) => BeginResize(window, edges, serial);
        toplevel.MaximizeRequested += maximized =>
        {
            toplevel.RequestConfigure();
            if (window.IsMapped && maximized != window.Maximized)
            {
                SetMaximized(window, maximized);
            }
            else if (!window.IsMapped)
            {
                _pendingMaximize[window] = maximized;
            }
        };
        toplevel.FullscreenRequested += fullscreen =>
        {
            toplevel.RequestConfigure();
            if (window.IsMapped && fullscreen != window.Fullscreen)
            {
                SetFullscreen(window, fullscreen);
            }
            else if (!window.IsMapped)
            {
                _pendingFullscreen[window] = fullscreen;
            }
        };
        toplevel.ShowWindowMenuRequested += (x, y) => ShowWindowMenu(window, window.ClientBox.X + x, window.ClientBox.Y + y);
        toplevel.WmCapabilities = XdgWmCapabilities.All;
    }

    private readonly Dictionary<ManagedWindow, bool> _pendingMaximize = [];
    private readonly Dictionary<ManagedWindow, bool> _pendingFullscreen = [];

    private void OnMapped(ManagedWindow window)
    {
        if (window.IsMapped)
        {
            return;
        }

        window.Workspace = _workspaces.Current;
        window.Minimized = false;
        window.Content.SetMinimized(false);
        _windows.Add(window);
        window.Map(LayerFor(window), Scale, window.Content.ServerDecorated);
        window.CreatePopupTree(Layers.Menu);
        if (window.Content.Surface is { } surface)
        {
            _host.Screens.EnterScreen(surface, OutputKey);
        }

        window.Content.SetBounds(WorkArea.Width, WorkArea.Height);
        Place(window);
        if (_pendingFullscreen.Remove(window, out var fullscreen) && fullscreen)
        {
            SetFullscreen(window, true);
        }
        else if (_pendingMaximize.Remove(window, out var maximized) && maximized)
        {
            SetMaximized(window, true);
        }

        var parentFocused = window.Content.Parent is { } parent && ReferenceEquals(OwnerOf(parent), _focused);
        var takesFocus = FocusPolicy.TakesFocusOnMap(
            _settings.FocusNewWindows,
            _focused is not null,
            window.UserTime,
            _focused?.UserTime ?? 0,
            parentFocused);
        if (_showingDesktop)
        {
            _showingDesktop = false;
            RestoreDesktop();
        }

        if (takesFocus)
        {
            Focus(window);
        }
        else
        {
            window.DemandsAttention = true;
            window.RefreshFrame();
        }

        IndexCapture(window);
        StackChanged();
        RequestIcon(window);
        NestedLog.Log.Debug($"mapped '{window.Title}' at {window.FrameBox.X},{window.FrameBox.Y} decorated={window.Decorated}");
        WindowMapped?.Invoke(window);
        Publish();
    }

    private void OnUnmapped(ManagedWindow window)
    {
        if (!window.IsMapped)
        {
            return;
        }

        if (_grabWindow == window)
        {
            EndGrab();
        }

        if (_openMenu is not null && ReferenceEquals(_menuOwner, window))
        {
            DismissOpenMenu();
        }

        _windows.Remove(window);
        _history.Remove(window);
        UnindexCapture(window);
        window.Unmap();
        StackChanged();
        NestedLog.Log.Debug($"unmapped '{window.Title}'");
        if (_focused == window)
        {
            _focused = null;
            Focus(NextFocusCandidate());
        }

        WindowUnmapped?.Invoke(window);
        Publish();
    }

    private void OnCommitted(ManagedWindow window)
    {
        if (!window.IsMapped)
        {
            return;
        }

        window.OnCommitted();
        PinCommittedGeometry(window);
        if (window.FrameBox != window.PublishedFrame)
        {
            Publish();
        }
    }

    private void PinCommittedGeometry(ManagedWindow window)
    {
        Box? anchor = window.Fullscreen ? Output
            : window.Maximized ? WorkArea
            : window.Tile != TileEdge.None ? Tiling.RectFor(window.Tile, WorkArea)
            : null;
        if (anchor is not { } box)
        {
            return;
        }

        var insets = window.Insets;
        var client = window.ClientBox;
        var x = box.X + insets.Left;
        var y = box.Y + insets.Top;
        if (client.X != x || client.Y != y)
        {
            window.MoveClientTo(x, y);
            window.RefreshFrame();
        }
    }

    private ManagedWindow? NextFocusCandidate()
    {
        foreach (var candidate in _history.Items)
        {
            if (candidate.IsMapped && !candidate.Minimized && IsOnCurrentWorkspace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void Place(ManagedWindow window)
    {
        var insets = window.Insets;
        var geometry = window.Geometry;
        var width = Math.Max(1, geometry.Width) + insets.Left + insets.Right;
        var height = Math.Max(1, geometry.Height) + insets.Top + insets.Bottom;
        Box? parentFrame = null;
        var parentTitle = 0;
        if (window.Content.Parent is { } parent && OwnerOf(parent) is { IsMapped: true } owner)
        {
            parentFrame = owner.FrameBox;
            parentTitle = owner.TitleHeight;
        }

        var visible = new List<Box>();
        foreach (var other in _windows)
        {
            if (!ReferenceEquals(other, window) && other.IsMapped && !other.Minimized && IsOnCurrentWorkspace(other))
            {
                visible.Add(other.FrameBox);
            }
        }

        var request = new PlacementRequest(
            width,
            height,
            WorkArea,
            visible,
            new Point((int)_cursorX, (int)_cursorY),
            _settings.Placement,
            _settings.CenterNewWindows,
            parentFrame,
            parentTitle,
            Output);
        var origin = window.Content.Centered
            ? new Point(WorkArea.X + ((WorkArea.Width - width) / 2), WorkArea.Y + ((WorkArea.Height - height) / 2))
            : Placement.Place(request);
        window.MoveFrameTo(origin.X, origin.Y);
        if (parentFrame is null
            && ((_settings.Placement == PlacementMode.Maximize && !window.Content.Centered)
                || Placement.ShouldMaximize(width, height, WorkArea)))
        {
            SetMaximized(window, true);
        }
        else if (_settings.Placement == PlacementMode.Manual && parentFrame is null)
        {
            BeginMove(window, null);
        }
    }

    private void RecordDecorationPreference(Surface surface, bool serverSide)
    {
        if (_decorationPreference.TryAdd(surface, serverSide))
        {
            surface.Destroyed += () => _decorationPreference.Remove(surface);
        }
        else
        {
            _decorationPreference[surface] = serverSide;
        }

        if (WindowOf(surface) is { IsMapped: true } window && window.Content.Surface == surface)
        {
            window.SetDecorated(serverSide);
            FitStateGeometry(window);
            Publish();
        }
    }

    private bool IsServerDecorated(XdgToplevelWindow toplevel)
    {
        if (_decorationPreference.TryGetValue(toplevel.Surface, out var serverSide))
        {
            return serverSide;
        }

        if (_kdeDecorations is not null
            && _kdeDecorations.ModeOf(toplevel.Surface) == KdeServerDecorationManager.DecorationMode.Server)
        {
            return true;
        }

        return _decorations is not null && _decorations.ModeOf(toplevel) == DecorationMode.ServerSide;
    }

    private SceneTree LayerFor(ManagedWindow window) =>
        window.Fullscreen ? Layers.Fullscreen : window.Above ? Layers.Above : Layers.Normal;

    private bool IsOnCurrentWorkspace(ManagedWindow window) => window.Sticky || window.Workspace == _workspaces.Current;

    private void ApplyWorkspaceVisibility()
    {
        foreach (var window in _windows)
        {
            var visible = !window.Minimized && IsOnCurrentWorkspace(window) && !(_showingDesktop && !window.Above);
            if (window.Tree is { } tree)
            {
                tree.Enabled = visible;
            }

            if (window.PopupTree is { } popups)
            {
                popups.Enabled = visible;
            }
        }
    }

    /// <summary>
    /// Gives focus to a window, or clears focus. It does not raise the window.
    /// It does nothing for a window that is not mapped or is minimized.
    /// </summary>
    /// <param name="window">The window, or <see langword="null"/> to clear focus.</param>
    public void Focus(ManagedWindow? window)
    {
        if (window is not null && (!window.IsMapped || window.Minimized))
        {
            return;
        }

        if (_focused == window)
        {
            if (window is not null)
            {
                _history.Touch(window);
            }

            return;
        }

        DismissOpenMenu();
        _focused?.SetActive(false);
        _focused = window;
        if (window is not null)
        {
            _history.Touch(window);
            window.SetActive(true);
            window.UserTime = Environment.TickCount64;
        }

        ApplyKeyboardFocus();
        Publish();
    }

    private void ApplyKeyboardFocus()
    {
        if (_panelHasKeyboard)
        {
            return;
        }

        if (_focused?.Content.UISurface is { } chrome)
        {
            _host.Seat.Keyboard.NotifyClearFocus();
            _router.SetKeyboardFocus(chrome, [.. _pressedKeys]);
        }
        else
        {
            _router.SetKeyboardFocus(null);
            if (_focused?.Content.Surface is { } surface)
            {
                _host.Seat.Keyboard.NotifyEnter(surface);
            }
            else
            {
                _host.Seat.Keyboard.NotifyClearFocus();
            }
        }
    }

    internal ManagedWindow? OwnerOf(IWindowContent content)
    {
        foreach (var candidate in _windows)
        {
            if (ReferenceEquals(candidate.Content, content))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Puts a window and its transients above the other windows in their layers.
    /// </summary>
    /// <param name="window">The window.</param>
    public void Raise(ManagedWindow window)
    {
        window.Tree?.RaiseToTop();
        window.Content.Raise();
        RaiseTransients(window);
        StackChanged();
    }

    private void RaiseTransients(ManagedWindow owner)
    {
        foreach (var candidate in _windows)
        {
            if (candidate.Content.Parent is { } parent && ReferenceEquals(OwnerOf(parent), owner) && candidate.Tree is { } tree)
            {
                tree.RaiseToTop();
                candidate.Content.Raise();
            }
        }
    }

    /// <summary>
    /// Puts a window below the other windows in its layer.
    /// </summary>
    /// <param name="window">The window.</param>
    public void Lower(ManagedWindow window)
    {
        window.Tree?.LowerToBottom();
        window.Content.Lower();
        StackChanged();
    }

    /// <summary>
    /// Activates the window of an xdg toplevel as <see cref="ActivateWindow"/> does. It does nothing for a toplevel that the shell does not manage.
    /// </summary>
    /// <param name="toplevel">The toplevel.</param>
    public void Activate(XdgToplevelWindow toplevel)
    {
        if (!_byToplevel.TryGetValue(toplevel, out var window))
        {
            return;
        }

        ActivateWindow(window);
    }

    /// <summary>
    /// Restores a minimized window, switches to its workspace, then focuses and raises it.
    /// </summary>
    /// <param name="window">The window.</param>
    public void ActivateWindow(ManagedWindow window)
    {
        if (window.Minimized)
        {
            SetMinimized(window, false);
        }

        if (!window.Sticky && window.Workspace != _workspaces.Current)
        {
            SwitchWorkspace(window.Workspace);
        }

        Focus(window);
        Raise(window);
    }

    /// <summary>
    /// Minimizes or restores a window. Minimizing the focused window moves focus to the next window. Restoring focuses and raises the window.
    /// It does nothing for a window that is not mapped or already in that state.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="minimized"><see langword="true"/> to minimize.</param>
    public void SetMinimized(ManagedWindow window, bool minimized)
    {
        if (window.Minimized == minimized || !window.IsMapped)
        {
            return;
        }

        window.Minimized = minimized;
        NestedLog.Log.Debug($"{(minimized ? "minimized" : "restored")} '{window.Title}'");
        window.Content.SetMinimized(minimized);
        ApplyWorkspaceVisibility();
        if (minimized && _focused == window)
        {
            _focused = null;
            window.SetActive(false);
            Focus(NextFocusCandidate());
        }
        else if (!minimized)
        {
            Focus(window);
            Raise(window);
        }

        Publish();
    }

    /// <summary>
    /// Maximizes a window to the work area, or restores it to its saved frame. Without a saved frame the content chooses its size.
    /// It does nothing for a window that is not mapped.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="maximized"><see langword="true"/> to maximize.</param>
    public void SetMaximized(ManagedWindow window, bool maximized)
    {
        if (!window.IsMapped)
        {
            return;
        }

        if (maximized)
        {
            if (!window.Maximized)
            {
                window.Restore = window.Tile == TileEdge.None ? window.FrameBox : window.Restore;
            }

            window.Tile = TileEdge.None;
            window.Content.SetTiled(ResizeEdges.None);
            window.Content.SetMaximized(true);
            ApplyMaximizedGeometry(window);
        }
        else
        {
            window.Content.SetMaximized(false);
            RestoreGeometry(window);
        }

        window.RefreshFrame();
        Publish();
    }

    internal void FitStateGeometry(ManagedWindow window)
    {
        if (window.Fullscreen)
        {
            return;
        }

        if (window.Maximized)
        {
            ApplyMaximizedGeometry(window);
        }
        else if (window.Tile != TileEdge.None)
        {
            ApplyTileGeometry(window, window.Tile);
        }
    }

    private void ApplyMaximizedGeometry(ManagedWindow window)
    {
        var insets = window.Insets;
        window.MoveFrameTo(WorkArea.X, WorkArea.Y);
        window.Content.SetSize(
            Math.Max(1, WorkArea.Width - insets.Left - insets.Right),
            Math.Max(1, WorkArea.Height - insets.Top - insets.Bottom));
    }

    private void RestoreGeometry(ManagedWindow window)
    {
        if (window.Restore is { } saved)
        {
            window.Restore = null;
            var insets = window.Insets;
            var frame = Constraints.FitWorkArea(saved, WorkArea);
            window.MoveFrameTo(frame.X, frame.Y);
            window.Content.SetSize(
                Math.Max(1, frame.Width - insets.Left - insets.Right),
                Math.Max(1, frame.Height - insets.Top - insets.Bottom));
        }
        else
        {
            window.Content.SetSize(0, 0);
        }
    }

    /// <summary>
    /// Moves a window to the fullscreen layer over the whole output, or back to its maximized or saved geometry. It raises the window.
    /// It does nothing for a window that is not mapped or already in that state.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="fullscreen"><see langword="true"/> for fullscreen.</param>
    public void SetFullscreen(ManagedWindow window, bool fullscreen)
    {
        if (!window.IsMapped || window.Fullscreen == fullscreen)
        {
            return;
        }

        if (fullscreen)
        {
            window.Restore ??= window.FrameBox;
            window.Content.SetFullscreen(true);
            window.Tree?.Reparent(Layers.Fullscreen);
            ApplyFullscreenGeometry(window);
        }
        else
        {
            window.Content.SetFullscreen(false);
            window.Tree?.Reparent(LayerFor(window));
            if (window.Maximized)
            {
                ApplyMaximizedGeometry(window);
            }
            else
            {
                RestoreGeometry(window);
            }
        }

        window.LayoutDecorations();
        Raise(window);
        Publish();
    }

    private void ApplyFullscreenGeometry(ManagedWindow window)
    {
        window.MoveClientTo(Output.X, Output.Y);
        window.Content.SetSize(Output.Width, Output.Height);
    }

    /// <summary>
    /// Tiles a window to the left or right half of the work area, maximizes it for <see cref="TileEdge.Top"/>, or restores it for <see cref="TileEdge.None"/>.
    /// It does nothing for a window that is not mapped.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="edge">The tile edge.</param>
    public void SetTile(ManagedWindow window, TileEdge edge)
    {
        if (!window.IsMapped)
        {
            return;
        }

        if (edge == TileEdge.Top)
        {
            SetMaximized(window, true);
            return;
        }

        if (edge == TileEdge.None)
        {
            if (window.Tile != TileEdge.None)
            {
                window.Tile = TileEdge.None;
                window.Content.SetTiled(ResizeEdges.None);
                RestoreGeometry(window);
                window.RefreshFrame();
            }

            return;
        }

        if (window.Tile == TileEdge.None && !window.Maximized)
        {
            window.Restore = window.FrameBox;
        }

        if (window.Maximized)
        {
            window.Content.SetMaximized(false);
        }

        window.Tile = edge;
        window.Content.SetTiled(edge == TileEdge.Left
            ? ResizeEdges.Left | ResizeEdges.Top | ResizeEdges.Bottom
            : ResizeEdges.Right | ResizeEdges.Top | ResizeEdges.Bottom);
        ApplyTileGeometry(window, edge);
        window.RefreshFrame();
        Publish();
    }

    private void ApplyTileGeometry(ManagedWindow window, TileEdge edge)
    {
        var rect = Tiling.RectFor(edge, WorkArea);
        var insets = window.Insets;
        window.MoveFrameTo(rect.X, rect.Y);
        window.Content.SetSize(
            Math.Max(1, rect.Width - insets.Left - insets.Right),
            Math.Max(1, rect.Height - insets.Top - insets.Bottom));
    }

    /// <summary>
    /// Rolls a window up to its title bar, or rolls it down. It does nothing for a window that is not mapped or already in that state.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="shaded"><see langword="true"/> to roll the window up.</param>
    public void SetShaded(ManagedWindow window, bool shaded)
    {
        if (window.Shaded == shaded || !window.IsMapped)
        {
            return;
        }

        window.Shaded = shaded;
        window.ApplyShade();
    }

    /// <summary>
    /// Moves a window to the above layer, or back to the normal layer, at the top of the layer.
    /// It does nothing for a window that is not mapped or already in that state.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="above"><see langword="true"/> to keep the window above the others.</param>
    public void SetAbove(ManagedWindow window, bool above)
    {
        if (window.Above == above || !window.IsMapped)
        {
            return;
        }

        window.Above = above;
        window.Tree?.Reparent(LayerFor(window));
        window.Tree?.RaiseToTop();
        window.RefreshFrame();
        StackChanged();
    }

    /// <summary>
    /// Shows a window on every workspace, or puts it on the current workspace only.
    /// It does nothing for a window that is not mapped or already in that state.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="sticky"><see langword="true"/> to show the window on every workspace.</param>
    public void SetSticky(ManagedWindow window, bool sticky)
    {
        if (window.Sticky == sticky || !window.IsMapped)
        {
            return;
        }

        window.Sticky = sticky;
        if (!sticky)
        {
            window.Workspace = _workspaces.Current;
        }

        window.RefreshFrame();
        ApplyWorkspaceVisibility();
        Publish();
    }

    /// <summary>
    /// Makes a workspace current, ends show-desktop and moves focus to a window on that workspace.
    /// It does nothing for an index out of range or for the current workspace.
    /// </summary>
    /// <param name="index">The zero-based workspace index.</param>
    public void SwitchWorkspace(int index)
    {
        if (index < 0 || index >= _workspaces.Count || index == _workspaces.Current)
        {
            return;
        }

        DismissOpenMenu();
        _workspaces.Switch(index);
        _showingDesktop = false;
        ApplyWorkspaceVisibility();
        if (_focused is { } focused && !IsOnCurrentWorkspace(focused))
        {
            _focused = null;
            focused.SetActive(false);
        }

        if (_focused is null)
        {
            Focus(NextFocusCandidate());
        }

        Publish();
    }

    /// <summary>
    /// Moves a window to a workspace and clears its sticky state. The window loses focus when it leaves the current workspace.
    /// It does nothing for an index out of range or a window that is not mapped.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="index">The zero-based workspace index.</param>
    public void MoveToWorkspace(ManagedWindow window, int index)
    {
        if (index < 0 || index >= _workspaces.Count || !window.IsMapped)
        {
            return;
        }

        window.Sticky = false;
        window.Workspace = index;
        ApplyWorkspaceVisibility();
        if (_focused == window && !IsOnCurrentWorkspace(window))
        {
            _focused = null;
            window.SetActive(false);
            Focus(NextFocusCandidate());
        }

        window.RefreshFrame();
        Publish();
    }

    /// <summary>
    /// Hides every window that is not above and clears focus, or shows the windows again and focuses the most recent one.
    /// </summary>
    public void ToggleShowDesktop()
    {
        _showingDesktop = !_showingDesktop;
        if (_showingDesktop)
        {
            _focused?.SetActive(false);
            _focused = null;
            _host.Seat.Keyboard.NotifyClearFocus();
        }

        ApplyWorkspaceVisibility();
        if (!_showingDesktop)
        {
            Focus(NextFocusCandidate());
        }

        Publish();
    }

    private void RestoreDesktop()
    {
        _showingDesktop = false;
        ApplyWorkspaceVisibility();
    }

    /// <summary>
    /// Asks the content of a window to close. The window stays until the content unmaps.
    /// </summary>
    /// <param name="window">The window.</param>
    public void CloseWindow(ManagedWindow window) => window.Content.Close();

    /// <summary>
    /// Manages content that is not an xdg toplevel and maps it at once. <see cref="Release"/> ends the management.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="clientIcon">The icon name of the content, or <see langword="null"/>.</param>
    /// <param name="publish"><see langword="true"/> to list the window in <see cref="IToplevelModel"/>. Use <see langword="false"/> when another source lists it already.</param>
    /// <returns>The new mapped window.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public ManagedWindow Adopt(IWindowContent content, string? clientIcon = null, bool publish = true)
    {
        ArgumentNullException.ThrowIfNull(content);
        var window = new ManagedWindow(this, content, ++_nextId, null) { ClientIcon = clientIcon };
        window.AdoptedCommitted = () => OnCommitted(window);
        content.Committed += window.AdoptedCommitted;
        content.TitleChanged += Publish;
        content.AppIdChanged += Publish;
        if (publish)
        {
            _ = _adopted.Add(window);
        }

        OnMapped(window);
        return window;
    }

    /// <summary>
    /// Unmaps a window that <see cref="Adopt"/> created, removes it from the toplevel model, and stops listening to its content.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public void Release(ManagedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.AdoptedCommitted is { } committed)
        {
            window.Content.Committed -= committed;
            window.Content.TitleChanged -= Publish;
            window.Content.AppIdChanged -= Publish;
            window.AdoptedCommitted = null;
        }

        OnUnmapped(window);
        _adopted.Remove(window);
    }

    /// <summary>
    /// Shows a surface in <see cref="ShellLayers.Menu"/> with no frame and no window state.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <param name="x">The horizontal position in logical layout coordinates.</param>
    /// <param name="y">The vertical position in logical layout coordinates.</param>
    /// <returns>The scene surface. The caller owns it and destroys it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public SceneSurface AddUnmanaged(Surface surface, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var scene = new SceneSurface(Layers.Menu, surface);
        scene.Tree.SetPosition(x, y);
        _host.Screens.EnterScreen(surface, OutputKey);
        return scene;
    }

    /// <summary>
    /// Runs an action that a frame asks for: close, maximize, minimize, move, resize, shade, above, sticky or the window menu.
    /// </summary>
    /// <param name="window">The window of the frame.</param>
    /// <param name="action">The action.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public void OnFrameAction(ManagedWindow window, FrameAction action)
    {
        ArgumentNullException.ThrowIfNull(window);
        switch (action.Kind)
        {
            case FrameActionKind.Close:
                CloseWindow(window);
                break;
            case FrameActionKind.ToggleMaximize:
                SetMaximized(window, !window.Maximized);
                break;
            case FrameActionKind.Minimize:
                SetMinimized(window, true);
                break;
            case FrameActionKind.Move:
                BeginMove(window, null);
                break;
            case FrameActionKind.Resize:
                BeginResize(window, (ResizeEdges)action.Edges, null);
                break;
            case FrameActionKind.ToggleShade:
                SetShaded(window, !window.Shaded);
                break;
            case FrameActionKind.ToggleAbove:
                SetAbove(window, !window.Above);
                break;
            case FrameActionKind.ToggleSticky:
                SetSticky(window, !window.Sticky);
                break;
            case FrameActionKind.ShowMenu:
                ShowWindowMenu(window, window.FrameBox.X, window.ClientBox.Y);
                break;
        }
    }

    internal void OnFrameFaulted(ManagedWindow window, Exception error) =>
        NestedLog.Log.Error($"the frame of '{window.Title}' failed: {error.Message}");

    /// <summary>
    /// Focuses and raises a window and opens its window menu. It does nothing for a window that is not mapped or has no frame.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="x">The horizontal menu position in logical layout coordinates.</param>
    /// <param name="y">The vertical menu position in logical layout coordinates.</param>
    public void ShowWindowMenu(ManagedWindow window, int x, int y)
    {
        if (window.Frame is not { } frame || !window.IsMapped)
        {
            return;
        }

        Focus(window);
        Raise(window);
        PrepareMenu(window, frame);
        frame.OpenMenu(x - window.X, y - window.Y);
        _openMenu = frame.IsMenuOpen ? frame : null;
        _menuOwner = frame.IsMenuOpen ? window : null;
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (popup.Parent is null)
        {
            return;
        }

        _popups.Attach(popup, PopupLayerFor(popup), origin: () => PopupContentOrigin(popup), constrainBox: () => Output);
        popup.Xdg.Mapped += () => _host.Screens.EnterScreen(popup.Surface, OutputKey);
    }

    private SceneTree PopupLayerFor(XdgPopupWindow popup)
    {
        var xdg = popup.Parent;
        while (xdg?.Role is XdgPopupWindow parentPopup)
        {
            xdg = parentPopup.Parent;
        }

        return xdg?.Role is XdgToplevelWindow toplevel && _byToplevel.TryGetValue(toplevel, out var window)
            && window.PopupTree is { } tree
            ? tree
            : Layers.Menu;
    }

    private Point PopupContentOrigin(XdgPopupWindow popup)
    {
        var x = 0;
        var y = 0;
        var xdg = popup.Parent;
        while (xdg is not null)
        {
            if (xdg.Role is XdgPopupWindow parentPopup)
            {
                xdg = parentPopup.Parent;
                continue;
            }

            var geometry = xdg.EffectiveGeometry;
            x += geometry.X;
            y += geometry.Y;
            if (xdg.Role is XdgToplevelWindow toplevel && _byToplevel.TryGetValue(toplevel, out var window))
            {
                x += window.X;
                y += window.Y;
            }

            return new Point(x, y);
        }

        return new Point(x, y);
    }

    /// <summary>
    /// Finds the mapped window that owns a surface, subsurfaces included.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <returns>The window, or <see langword="null"/>.</returns>
    public ManagedWindow? WindowOf(Surface surface)
    {
        foreach (var window in _windows)
        {
            if (window.Owns(surface))
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a mapped window by its shell-local id.
    /// </summary>
    /// <param name="id">The id from <see cref="ManagedWindow.Id"/>.</param>
    /// <returns>The window, or <see langword="null"/>.</returns>
    public ManagedWindow? WindowById(long id)
    {
        foreach (var window in _windows)
        {
            if (window.Id == id)
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// Lists the mapped windows on the current workspace for switching, the most recently focused first. Minimized windows are included.
    /// </summary>
    /// <param name="sameGroup"><see langword="true"/> to keep only the windows with the app id of the focused window.</param>
    /// <returns>A new list.</returns>
    public IReadOnlyList<ManagedWindow> SwitchOrder(bool sameGroup)
    {
        var order = new List<ManagedWindow>();
        foreach (var candidate in _history.Items)
        {
            if (candidate.IsMapped && IsOnCurrentWorkspace(candidate)
                && (!sameGroup || _focused is null || candidate.Content.AppId == _focused.Content.AppId))
            {
                order.Add(candidate);
            }
        }

        foreach (var candidate in _windows)
        {
            if (candidate.IsMapped && IsOnCurrentWorkspace(candidate) && !order.Contains(candidate)
                && (!sameGroup || _focused is null || candidate.Content.AppId == _focused.Content.AppId))
            {
                order.Add(candidate);
            }
        }

        return order;
    }

    /// <summary>
    /// Refreshes the adopted toplevels and raises <see cref="Changed"/>. It does nothing after dispose.
    /// </summary>
    public void Publish()
    {
        if (_disposed)
        {
            return;
        }

        if (_adopted.Count > 0)
        {
            _adopted.Refresh();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Takes a snapshot of the mapped windows for a panel, in map order. It records each frame box in <see cref="ManagedWindow.PublishedFrame"/>.
    /// </summary>
    /// <returns>A new list.</returns>
    public IReadOnlyList<PanelWindowInfo> SnapshotWindows()
    {
        var list = new List<PanelWindowInfo>(_windows.Count);
        foreach (var window in _windows)
        {
            if (!window.IsMapped)
            {
                continue;
            }

            var frame = window.FrameBox;
            window.PublishedFrame = frame;
            list.Add(new PanelWindowInfo(
                window.Id,
                window.Title,
                window.Content.AppId,
                window.Suffix,
                window.Workspace,
                window.Sticky,
                ReferenceEquals(window, _focused),
                window.Minimized,
                window.DemandsAttention,
                frame,
                window.IconName is { } icon && Path.IsPathRooted(icon) ? icon : null));
        }

        return list;
    }

    /// <summary>
    /// Takes a snapshot of the workspaces for a panel, in index order.
    /// </summary>
    /// <returns>A new list.</returns>
    public IReadOnlyList<PanelWorkspaceInfo> SnapshotWorkspaces()
    {
        var list = new List<PanelWorkspaceInfo>(_workspaces.Count);
        for (var i = 0; i < _workspaces.Count; i++)
        {
            list.Add(new PanelWorkspaceInfo(i, _workspaces.Names[i]));
        }

        return list;
    }

    /// <summary>
    /// Disconnects from the host, unmaps every window and releases the frames, the UI host and the capture renderer. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ReferenceEquals(_host.Seat.SyntheticInput, _syntheticInput))
        {
            _host.Seat.SyntheticInput = null;
        }

        _host.Shell.NewToplevel -= OnNewToplevel;
        _host.Shell.NewPopup -= OnNewPopup;
        _host.Seat.Pointer.CursorRequested -= OnCursorRequested;
        DetachPointerConstraints();
        foreach (var window in _windows.ToArray())
        {
            window.Unmap();
        }

        _windows.Clear();
        DisposeControl();
        _background.Destroy();
        _frames.Dispose();
        UIHost.Dispose();
    }

    private static RenderColor BackgroundColor(ShellSettings settings) =>
        ParseColor(settings.Background) ?? ParseColor(ShellSettings.DefaultBackground)!.Value;

    private static RenderColor? ParseColor(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length is not (4 or 7) || trimmed[0] != '#' || !trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            return null;
        }

        var hex = trimmed.Length == 4
            ? string.Create(CultureInfo.InvariantCulture, $"#{trimmed[1]}{trimmed[1]}{trimmed[2]}{trimmed[2]}{trimmed[3]}{trimmed[3]}")
            : trimmed;
        return new RenderColor(Channel(hex, 1), Channel(hex, 3), Channel(hex, 5), 1f);
    }

    private static float Channel(string hex, int start) =>
        int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
}
