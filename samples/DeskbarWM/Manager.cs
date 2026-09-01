using Basin.Config;
using InputCodes = Basin.InputCodes;
using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Protocol = Basin.WindowManager.Skia.Protocol;
using CursorShape = Basin.WindowManager.Protocol.WpCursorShapeDeviceV1.Shape;
using SkiaSharp;
using Wayland;

using Basin.Diagnostics;

namespace DeskbarWm;

internal sealed class Manager
{
    private const int CascadePadding = 32;
    private const long DoubleClickMs = 400;

    private readonly RiverWindowManager _wm;
    private readonly WlCompositor? _compositor;
    private readonly WlShm? _shm;
    private readonly OutputScales? _scales;
    private Protocol.ZwlrLayerShellV1? _layerShell;
    private readonly bool _trace;
    private readonly WmSession<ManagedWindow> _session;
    private readonly WmFocusStack<ManagedWindow> _focusStack;
    private readonly IReadOnlyList<ManagedWindow> _windows;
    private readonly IReadOnlyDictionary<WmWindow, ManagedWindow> _byWindow;
    private readonly List<WmOutput> _outputs = [];
    private readonly Queue<WmAction> _actions = new();
    private readonly List<KeyBinding> _bindings = [];
    private readonly HashSet<WmSeat> _armedSeats = [];
    private readonly List<ManagedWindow> _cascade = [];
    private readonly Dictionary<WmOutput, int> _cascadeSteps = [];

    private readonly PointerInput _pointerInput;
    private readonly TeamTable _teams = new();
    private readonly Dictionary<WmOutput, DeskbarSurface> _deskbars = [];
    private readonly Dictionary<WmOutput, WallpaperSurface> _wallpapers = [];
    private WallpaperSurface? _pointerWallpaper;
    private readonly Queue<Team> _teamActivations = new();
    private readonly Queue<ManagedWindow> _windowActivations = new();
    private readonly Queue<Team> _arrowToggles = new();
    private readonly List<BarRow> _entryScratch = [];
    private readonly IconCache _icons = new(new IconRaster(new Basin.Cli.IconSearch
    {
        OverrideDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "deskbar-wm", "icons"),
    }.Find));
    private DeskbarSurface? _pointerBar;
    private (DeskbarSurface Bar, DeskbarPlacement? Candidate)? _barDrag;
    private PlacementOutline? _outline;
    private readonly ClockApplet _clock;
    private readonly WorkspacesApplet _workspacesApplet = new();
    private readonly WorkspaceGrid _workspaces = new();
    private readonly SatTable _sat = new();
    private DropHighlight? _dropHighlight;
    private SwitcherState? _switcher;
    private SwitcherSurface? _switcherSurface;
    private WmSubmap? _switcherSubmap;
    private readonly List<KeyBinding> _switcherBindings = [];
    private bool _switcherArmed;
    private readonly Queue<(int Index, bool TakeWindow)> _workspaceSwitches = new();
    private readonly Queue<Hotkey> _hotkeyPresses = new();
    private CalendarPopup? _calendar;
    private bool _pointerOnCalendar;
    private readonly List<MenuSurface> _menus = [];
    private MenuSurface? _pointerMenu;
    private readonly Queue<Action> _menuActions = new();
    private readonly RecentItems _recents;
    private Basin.WindowManager.IWmEventSource? _clockTimer;
    private ManagedWindow? _pointerTab;
    private double _tabX;
    private double _tabY;
    private (ManagedWindow Window, long At)? _lastTabClick;

    private WmSeat? _currentSeat;
    private WmOutput? _currentOutput;
    private DragState? _drag;
    private SKFont? _font;
    private readonly Dictionary<WmSeat, bool> _shiftHeld = [];
    private readonly Dictionary<WmSeat, bool> _ctrlHeld = [];
    private WmWindow? _lastPointerFocus;

    private readonly bool _noConfig;
    private readonly BasinLogger _log;
    private Config _config;
    private readonly WmReloadSignal _reload;

    internal Manager(RiverWindowManager wm, bool trace, bool noConfig, BasinLogger log)
    {
        _wm = wm;
        _trace = trace;
        _noConfig = noConfig;
        _log = log;
        _config = Config.Load(noConfig, log);
        _icons.Reconfigure(_config);
        _recents = new RecentItems(log);
        _clock = new ClockApplet(_config);
        _workspaces.Configure(_config.WorkspaceRows, _config.WorkspaceColumns);
        _focusStack = new WmFocusStack<ManagedWindow>(wm);
        _session = new WmSession<ManagedWindow>(_focusStack, window => new ManagedWindow(window));
        _windows = _session.Windows;
        _byWindow = _session.ByWindow;
        _session.Adopted += OnAdopted;
        _session.Forgetting += OnForgetting;
        _session.Interaction += OnInteraction;
        _session.PointerRequest += OnPointerRequest;
        _reload = new WmReloadSignal(wm);
        _reload.Reload += OnReload;
        if (wm is { Compositor: { } compositor, Shm: { } shm })
        {
            _compositor = compositor;
            _shm = shm;
            _scales = new OutputScales(wm);

            var registry = wm.Display.GetRegistry();
            registry.Global += (_, e) =>
            {
                if (e.Interface == "zwlr_layer_shell_v1")
                {
                    _layerShell = registry.Bind<Protocol.ZwlrLayerShellV1>(e.Name, Math.Min(e.Version, 4));
                }
            };
        }

        _pointerInput = new PointerInput(wm);
        _pointerInput.SurfaceEntered += OnDecorEntered;
        _pointerInput.SurfaceLeft += OnDecorLeft;
        _pointerInput.PointerMoved += OnDecorMotion;
        _pointerInput.ButtonChanged += OnDecorButton;

        wm.Bindings.ModifiersChanged += (seat, _, now) =>
        {
            _shiftHeld[seat] = (now & Modifiers.Shift) != 0;
            _ctrlHeld[seat] = (now & Modifiers.Ctrl) != 0;
            if (_switcher is not null && (now & Modifiers.Ctrl) == 0)
            {
                _actions.Enqueue(WmAction.SwitcherCommit);
                wm.RequestManage();
            }
        };

        _clockTimer = wm.Loop.AddTimer(OnClockTick);
        _clockTimer.UpdateTimer(ClockIntervalMs());

        wm.Manage += OnManage;
        wm.Render += OnRender;
        if (wm.LayerShell is { } layerShell)
        {
            layerShell.FocusTaken += _ => _actions.Enqueue(WmAction.ClearFocus);
            layerShell.FocusReleased += _ => _actions.Enqueue(WmAction.RestoreFocus);
        }
    }

    private SKFont Font => _font ??= new SKFont(Fonts.Sans, Theme.FontSize);

    private int ClockIntervalMs() => _config.ClockShowSeconds ? 1000 : 15000;

    private void OnClockTick()
    {
        _clockTimer?.UpdateTimer(ClockIntervalMs());
        if (HasClockApplet())
        {
            RenderDeskbars();
        }
    }

    private bool HasClockApplet()
    {
        foreach (var name in _config.TrayApplets)
        {
            if (name == "clock")
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshTrayApplets(DeskbarSurface bar)
    {
        var applets = new List<IApplet>();
        foreach (var name in _config.TrayApplets)
        {
            switch (name)
            {
                case "clock":
                    applets.Add(_clock);
                    break;
                case "workspaces":
                    applets.Add(_workspacesApplet);
                    break;
                default:
                    _log.Warn($"unknown tray applet '{name}'");
                    break;
            }
        }

        bar.Tray.SetApplets(applets);
    }

    private void OnManage(ManageContext context)
    {
        _reload.Process();
        UpdateCurrents(context);
        ApplyFocusMode();
        _session.AdoptNewWindows(context);
        _session.ForgetClosedWindows(context);
        SyncAllDimensions();
        _teams.Refresh(_windows);
        RefreshChrome();
        RefreshCapabilities();
        ArmBindings(context);
        ApplyDrag(allowResize: true);
        FinishReleasedDrag();
        _session.DrainInteractions();
        _session.DrainPointerRequests();
        DrainTeamActivations();
        DrainWorkspaceSwitches();
        DrainMenuActions();
        DrainActions();
        DrainWindowEvents();
        ApplyTiledStates();
        ApplyGroups(allowResize: true);
        ReassociateOutputs();
        Trace(context);
    }

    private void ApplyTiledStates()
    {
        foreach (var mw in _windows)
        {
            if (mw.Window.IsClosed)
            {
                continue;
            }

            mw.Window.SetTiled(
                mw.Area is not null
                    ? Edges.Left | Edges.Right | Edges.Top | Edges.Bottom
                    : Edges.None);
        }
    }

    private void OnRender(RenderContext context)
    {
        RefreshOutputs(context.Outputs);
        SyncAllDimensions();
        ApplyDrag(allowResize: false);
        ApplyInitialPositions();

        ApplyGroups(allowResize: false);

        foreach (var mw in _windows)
        {
            var stackedBehind = mw.Area is { Windows.Count: > 1 } area && !ReferenceEquals(area.Front, mw);
            if (mw.Hidden || !OnCurrentWorkspace(mw) || stackedBehind)
            {
                mw.Window.Hide();
            }
            else
            {
                mw.Window.Show();
            }
        }

        RenderDecorations();
        RenderDeskbars();
        RenderPlacementOutline();
        RenderSwitcher();
        RenderMenus();
        RenderDropHighlight();
        Restack();
    }

    private void UpdateCurrents(ManageContext context)
    {
        RefreshOutputs(context.Outputs);

        if (_currentSeat is null or { IsRemoved: true })
        {
            _currentSeat = context.Seats.Count > 0 ? context.Seats[0] : null;
        }

        if (_currentOutput is null or { IsRemoved: true })
        {
            _currentOutput = _outputs.Count > 0 ? _outputs[0] : null;
        }

        _session.ObserveSeats(context);
        _focusStack.Seat = _currentSeat;

        if (_wm.LayerShell is { } shell)
        {
            foreach (var output in _outputs)
            {
                shell.Track(output);
            }

            foreach (var seat in context.Seats)
            {
                shell.Track(seat);
            }
        }

        if (_compositor is not null && _shm is not null && _layerShell is not null && _scales is not null)
        {
            foreach (var output in _outputs)
            {
                if (_deskbars.ContainsKey(output) || _scales.ProxyForName(output.WlOutputName) is not { } proxy)
                {
                    continue;
                }

                _deskbars[output] = new DeskbarSurface(_compositor, _shm, _layerShell, proxy, output, _wm)
                {
                    AutoHidden = _config.AutoHide,
                };
                _wallpapers[output] = new WallpaperSurface(_compositor, _shm, _layerShell, proxy, output, _wm);
            }

            List<WmOutput>? removed = null;
            foreach (var (output, bar) in _deskbars)
            {
                if (output.IsRemoved)
                {
                    bar.Dispose();
                    (removed ??= []).Add(output);
                }
            }

            if (removed is not null)
            {
                foreach (var output in removed)
                {
                    _deskbars.Remove(output);
                    if (_wallpapers.Remove(output, out var wallpaper))
                    {
                        wallpaper.Dispose();
                    }
                }
            }
        }
    }

    private void RefreshOutputs(IReadOnlyList<WmOutput> outputs)
    {
        _outputs.Clear();
        foreach (var output in outputs)
        {
            _outputs.Add(output);
        }
    }

    private void OnAdopted(ManagedWindow mw)
    {
        var window = mw.Window;
        if (_compositor is not null && _shm is not null && _scales is not null)
        {
            mw.Tab = new TabSurface(window, _compositor, _shm, _scales);
        }

        mw.Events.Enqueue((WindowEventKind.Init, null));

        window.MaximizeRequested += () => mw.Events.Enqueue((WindowEventKind.Maximize, null));
        window.UnmaximizeRequested += () => mw.Events.Enqueue((WindowEventKind.Unmaximize, null));
        window.MinimizeRequested += () => mw.Events.Enqueue((WindowEventKind.Minimize, null));
        window.FullscreenRequested += output => mw.Events.Enqueue((WindowEventKind.Fullscreen, output));
        window.ExitFullscreenRequested += () => mw.Events.Enqueue((WindowEventKind.Unfullscreen, null));
    }

    private void OnForgetting(ManagedWindow mw)
    {
        if (_drag is { } drag && ReferenceEquals(drag.Window, mw))
        {
            _drag = null;
            drag.Op.End();
        }

        if (ReferenceEquals(_pointerTab, mw))
        {
            _pointerTab = null;
        }

        if (_lastTabClick is { } click && ReferenceEquals(click.Window, mw))
        {
            _lastTabClick = null;
        }

        if (mw.Area is { } area)
        {
            var group = area.Group;
            _sat.Remove(mw);
            ApplyGroup(group, allowResize: true);
        }

        mw.Tab?.Dispose();
    }

    private void SyncAllDimensions()
    {
        foreach (var mw in _windows)
        {
            mw.SyncDimensions();
        }
    }

    private void RefreshChrome()
    {
        foreach (var mw in _windows)
        {
            var acceptsAFrame = mw.Window.DecorationHint != DecorationHint.OnlySupportsClientSide;
            mw.Ssd = mw.Tab is not null && acceptsAFrame;
            if (mw.SentSsd != mw.Ssd)
            {
                mw.SentSsd = mw.Ssd;
                if (mw.Ssd)
                {
                    mw.Window.UseServerSideDecorations();
                }
                else
                {
                    mw.Window.UseClientSideDecorations();
                }
            }

        }
    }

    private TabMetrics ComputeMetrics(ManagedWindow mw) => TabMetrics.Compute(
        mw.Width,
        mw.Height,
        mw.Feel,
        mw.Window.Title,
        Font,
        mw.TabLocation,
        closable: true,
        zoomable: mw.Feel != WindowFeel.Modal && !mw.IsFixedSize);

    private void RefreshCapabilities()
    {
        foreach (var mw in _windows)
        {
            var capabilities = WindowCapabilities.WindowMenu;
            if (!mw.IsDialog)
            {
                capabilities |= WindowCapabilities.Minimize;
                if (!mw.IsFixedSize)
                {
                    capabilities |= WindowCapabilities.Maximize | WindowCapabilities.Fullscreen;
                }
            }

            if (capabilities == mw.SentCapabilities)
            {
                continue;
            }

            mw.SentCapabilities = capabilities;
            mw.Window.SetCapabilities(capabilities);
        }
    }

    private void RenderDecorations()
    {
        if (_compositor is null)
        {
            return;
        }

        foreach (var mw in _windows)
        {
            if (mw.Tab is not { } tab)
            {
                continue;
            }

            var stackedBehind = mw.Area is { Windows.Count: > 1 } behindArea
                && !ReferenceEquals(behindArea.Front, mw);
            if (mw.Hidden || !mw.Ssd || mw.FullscreenOutput is not null || stackedBehind)
            {
                if (tab.Mapped)
                {
                    tab.SyncNextCommit();
                    tab.Unmap();
                }

                continue;
            }

            if (mw.Width <= 0 || mw.Height <= 0)
            {
                continue;
            }

            mw.Metrics = ComputeMetrics(mw);
            var scale = tab.ScaleFor(mw.Output?.WlOutputName ?? 0);
            tab.EnsureBuffer(mw.Width, mw.Height, scale, mw.Metrics);
            List<(string Title, bool Front)>? strip = null;
            if (mw.Area is { Windows.Count: > 1 } stackArea && ReferenceEquals(stackArea.Front, mw))
            {
                strip = [];
                foreach (var member in stackArea.Windows)
                {
                    strip.Add((member.Window.Title ?? member.Window.AppId ?? "?", ReferenceEquals(member, mw)));
                }
            }

            tab.UpdateInputRegion(_compositor, strip?.Count ?? 0);

            var rendered = tab.Render(
                mw.Window.Title,
                ReferenceEquals(mw, _focusStack.Focused),
                mw.TabLeftDown ? mw.TabPressed : null,
                mw.Feel,
                strip);
            tab.SetOffset();
            if (rendered)
            {
                tab.SyncNextCommit();
                tab.Commit();
            }
        }
    }

    private void RenderDeskbars()
    {
        if (_deskbars.Count == 0)
        {
            return;
        }

        var teams = _teams.Teams(_config.SortTeams);
        _entryScratch.Clear();
        foreach (var team in teams)
        {
            if (team.AppId is { Length: > 0 } appId)
            {
                team.ResolvedName ??= DesktopEntries.NameFor(appId);
            }

            var active = _focusStack.Focused is { } focused && team.Windows.Contains(focused);
            var hidden = true;
            foreach (var mw in team.Windows)
            {
                if (!mw.Hidden)
                {
                    hidden = false;
                    break;
                }
            }

            var expanded = _config.ExpandWindows && (team.Expanded ?? _config.ExpandNewTeams);
            var icon = team.AppId is { Length: > 0 } id ? _icons.Load(id, _config.IconSize) : null;
            _entryScratch.Add(new TeamEntry(team, team.DisplayName, icon, active, hidden, expanded));
            if (expanded && _config.Placement.Orientation == BarOrientation.Vertical)
            {
                foreach (var mw in team.Windows)
                {
                    _entryScratch.Add(new WindowEntry(
                        mw,
                        mw.Window.Title ?? mw.Window.AppId ?? "?",
                        mw.Hidden,
                        ReferenceEquals(_focusStack.Focused, mw)));
                }
            }
        }

        foreach (var (output, bar) in _deskbars)
        {
            RefreshTrayApplets(bar);
            _workspacesApplet.Update(_workspaces, output.Area, _windows);
            var scale = _scales?.ScaleForName(output.WlOutputName) ?? 1;
            if (bar.Render(_entryScratch, scale, _config))
            {
                bar.Commit();
            }
        }

        if (_calendar is { } calendar)
        {
            var scale = _scales?.ScaleForName(calendar.Output.WlOutputName) ?? 1;
            if (calendar.Render(scale))
            {
                calendar.Commit();
            }
        }

        foreach (var (output, wallpaper) in _wallpapers)
        {
            var scale = _scales?.ScaleForName(output.WlOutputName) ?? 1;
            if (wallpaper.Render(_config, scale))
            {
                wallpaper.Commit();
            }
        }
    }

    private void RenderPlacementOutline()
    {
        if (_barDrag is not { Candidate: { } candidate } drag
            || _compositor is null || _shm is null || _layerShell is null || _scales is null)
        {
            if (_outline is not null)
            {
                _outline.Dispose();
                _outline = null;
            }

            return;
        }

        var output = drag.Bar.Output;
        if (_outline is not null && !ReferenceEquals(_outline.Output, output))
        {
            _outline.Dispose();
            _outline = null;
        }

        if (_outline is null)
        {
            if (_scales.ProxyForName(output.WlOutputName) is not { } proxy)
            {
                return;
            }

            _outline = new PlacementOutline(_compositor, _shm, _layerShell, proxy, output, _wm);
        }

        var scale = _scales.ScaleForName(output.WlOutputName);
        if (_outline.Render(EstimateFrame(candidate, output), scale))
        {
            _outline.Commit();
        }
    }

    private Rect EstimateFrame(DeskbarPlacement placement, WmOutput output)
    {
        var area = output.Area;
        var width = _config.DeskbarWidth > 0
            ? _config.DeskbarWidth
            : VerticalLayout.DefaultWidth(_config.IconSize);
        int frameWidth;
        int frameHeight;
        if (placement.Orientation == BarOrientation.Horizontal)
        {
            frameHeight = placement.State == DeskbarState.Mini
                ? HorizontalLayout.MinHeight
                : Math.Max(HorizontalLayout.MinHeight, _config.IconSize + 8);
            frameWidth = placement.State == DeskbarState.Mini
                ? HorizontalLayout.LeafWidth + 1 + DragHandle.Thickness
                : area.Width;
        }
        else
        {
            frameWidth = width;
            var layout = VerticalLayout.Compute(
                width, _config.IconSize, Theme.FontSize, _teams.Teams(false).Count, trayHeight: 0);
            frameHeight = placement.State switch
            {
                DeskbarState.Mini => VerticalLayout.MenuBarHeight + DragHandle.Thickness,
                DeskbarState.Full => area.Height,
                _ => Math.Max(layout.ContentHeight, VerticalLayout.MenuBarHeight) + DragHandle.Thickness,
            };
        }

        var x = placement.Side == BarSide.Left ? area.X : area.Right - frameWidth;
        var y = placement.End == BarEnd.Top ? area.Y : area.Bottom - frameHeight;
        if (placement.Orientation == BarOrientation.Horizontal && placement.State == DeskbarState.Expando)
        {
            x = area.X;
        }

        if (placement.Orientation == BarOrientation.Vertical && placement.State != DeskbarState.Mini)
        {
            y = area.Y;
        }

        return new Rect(x, y, frameWidth, frameHeight);
    }

    private void DrainTeamActivations()
    {
        while (_teamActivations.TryDequeue(out var team))
        {
            ActivateOrHideTeam(team);
        }

        while (_windowActivations.TryDequeue(out var mw))
        {
            if (!mw.Window.IsClosed)
            {
                Focus(mw);
            }
        }

        while (_arrowToggles.TryDequeue(out var toggled))
        {
            toggled.Expanded = !(toggled.Expanded ?? _config.ExpandNewTeams);
        }
    }

    private void ActivateOrHideTeam(Team team)
    {
        if (team.Windows.Count == 0)
        {
            return;
        }

        var isFront = _focusStack.Focused is { } focused && team.Windows.Contains(focused);
        if (isFront)
        {
            foreach (var mw in team.Windows)
            {
                mw.Hidden = true;
            }

            if (!_focusStack.RestoreFromStack(static other => !other.Hidden))
            {
                _focusStack.ClearFocus();
            }

            return;
        }

        ManagedWindow? front = null;
        foreach (var candidate in _focusStack)
        {
            if (team.Windows.Contains(candidate))
            {
                front = candidate;
                break;
            }
        }

        front ??= team.Windows[0];
        for (var i = team.Windows.Count - 1; i >= 0; i--)
        {
            var mw = team.Windows[i];
            if (!ReferenceEquals(mw, front))
            {
                Focus(mw);
            }
        }

        Focus(front);
    }

    private void ArmBindings(ManageContext context)
    {
        if (!_wm.Bindings.IsSupported)
        {
            return;
        }

        foreach (var seat in context.Seats)
        {
            if (!_armedSeats.Add(seat))
            {
                continue;
            }

            if (_wm.Bindings.Version >= 3)
            {
                _wm.Bindings.WatchModifiers(seat, Modifiers.Shift | Modifiers.Ctrl);
            }

            Bind(seat, "q", Modifiers.Ctrl | Modifiers.Alt, WmAction.Close);
            Bind(seat, "Return", Modifiers.Ctrl | Modifiers.Alt, WmAction.SpawnTerminal);
            Bind(seat, "m", Modifiers.Ctrl | Modifiers.Alt, WmAction.MinimizeFocused);

            BindPointer(seat, InputCodes.BtnLeft, Modifiers.Ctrl | Modifiers.Alt, WmAction.PointerMove);
            BindPointer(seat, InputCodes.BtnRight, Modifiers.Ctrl | Modifiers.Alt, WmAction.PointerResize);

            for (var i = 0; i < 12; i++)
            {
                var index = i;
                BindAction(seat, $"F{i + 1}", Modifiers.Alt,
                    () => _workspaceSwitches.Enqueue((index, false)));
                BindAction(seat, $"F{i + 1}", Modifiers.Alt | Modifiers.Shift,
                    () => _workspaceSwitches.Enqueue((index, true)));
            }

            BindAction(seat, "grave", Modifiers.Alt,
                () => _workspaceSwitches.Enqueue((_workspaces.Previous, false)));
            Bind(seat, "Tab", Modifiers.Ctrl, WmAction.SwitcherNextTeam);
            Bind(seat, "Tab", Modifiers.Ctrl | Modifiers.Shift, WmAction.SwitcherPreviousTeam);
            Bind(seat, "grave", Modifiers.Ctrl, WmAction.SwitcherNextWindow);
            BindAction(seat, "Left", Modifiers.Ctrl | Modifiers.Alt,
                () => _workspaceSwitches.Enqueue((_workspaces.Moved(-1, 0), false)));
            BindAction(seat, "Right", Modifiers.Ctrl | Modifiers.Alt,
                () => _workspaceSwitches.Enqueue((_workspaces.Moved(1, 0), false)));
            BindAction(seat, "Up", Modifiers.Ctrl | Modifiers.Alt,
                () => _workspaceSwitches.Enqueue((_workspaces.Moved(0, -1), false)));
            BindAction(seat, "Down", Modifiers.Ctrl | Modifiers.Alt,
                () => _workspaceSwitches.Enqueue((_workspaces.Moved(0, 1), false)));

            foreach (var hotkey in _config.Hotkeys)
            {
                var pressed = hotkey;
                var binding = _wm.Bindings.Bind(seat, pressed.Keysym, pressed.ModifierMask, () =>
                {
                    _hotkeyPresses.Enqueue(pressed);
                    _wm.RequestManage();
                });
                binding.Enable();
                _bindings.Add(binding);
            }
        }
    }

    private void BindAction(WmSeat seat, string keysym, Modifiers modifiers, Action action)
    {
        var binding = _wm.Bindings.Bind(seat, keysym, modifiers, action);
        binding.Enable();
        _bindings.Add(binding);
    }

    private void Bind(WmSeat seat, string keysym, Modifiers modifiers, WmAction action)
    {
        if (HotkeyShadowsDefault(keysym, modifiers))
        {
            return;
        }

        var binding = _wm.Bindings.Bind(seat, keysym, modifiers, () => _actions.Enqueue(action));
        binding.Enable();
        _bindings.Add(binding);
    }

    private bool HotkeyShadowsDefault(string keysym, Modifiers modifiers)
    {
        var symbol = Keysym.FromName(keysym);
        foreach (var hotkey in _config.Hotkeys)
        {
            if (hotkey.Keysym == symbol && hotkey.ModifierMask == modifiers)
            {
                return true;
            }
        }

        return false;
    }

    private void BindPointer(WmSeat seat, uint button, Modifiers modifiers, WmAction action)
    {
        var binding = seat.BindPointer(button, modifiers, () => _actions.Enqueue(action));
        binding.Enable();
    }

    private void OnInteraction(ManagedWindow mw)
    {
        CloseMenus();
        Focus(mw);
        HandleInteraction(mw);
    }

    private void HandleInteraction(ManagedWindow mw)
    {
        if (_currentSeat is not { IsRemoved: false } seat || _drag is { Op.IsEnded: false })
        {
            return;
        }

        if (!mw.Ssd)
        {
            return;
        }

        var frame = mw.FrameRect;
        var pointer = seat.PointerPosition;
        var localX = pointer.X - frame.X;
        var localY = pointer.Y - frame.Y;

        if (mw.Area is { Windows.Count: > 1 } stackArea && localY >= 0 && localY < mw.Metrics.TabHeight)
        {
            var slot = TabStrip.SlotAt(
                mw.Metrics.FrameWidth, mw.Metrics.TabHeight, stackArea.Windows.Count, localX, localY);
            if (slot >= 0 && slot < stackArea.Windows.Count)
            {
                var slotWindow = stackArea.Windows[slot];
                if (!ReferenceEquals(slotWindow, mw))
                {
                    RaiseInStack(stackArea, slotWindow);
                    return;
                }

                var slotRect = TabStrip.Slot(
                    mw.Metrics.FrameWidth, mw.Metrics.TabHeight, stackArea.Windows.Count, slot);
                var point = new Point(localX, localY);
                if (TabStrip.CloseRect(slotRect, mw.Metrics).Contains(point)
                    || TabStrip.ZoomRect(slotRect, mw.Metrics).Contains(point))
                {
                    return;
                }

                if (ShiftHeld(seat))
                {
                    _sat.Remove(mw);
                    StartDrag(mw, seat, Edges.None, fromTab: true);
                    return;
                }

                StartDrag(mw, seat, Edges.None, fromTab: true);
                return;
            }
        }

        var hit = mw.Metrics.HitTest(localX, localY, mw.Width, mw.Height);
        switch (hit.Part)
        {
            case FramePart.Tab:
                if (ShiftHeld(seat))
                {
                    StartSlide(mw, seat);
                }
                else
                {
                    StartDrag(mw, seat, Edges.None, fromTab: true);
                }

                break;
            case FramePart.Border when !mw.IsFixedSize && hit.Edges != Edges.None:
                StartDrag(mw, seat, hit.Edges);
                break;
            case FramePart.Border:
                StartDrag(mw, seat, Edges.None);
                break;
            case FramePart.ResizeCorner when !mw.IsFixedSize:
                StartDrag(mw, seat, Edges.Right | Edges.Bottom);
                break;
        }
    }

    private bool ShiftHeld(WmSeat seat) => _shiftHeld.GetValueOrDefault(seat);

    private void ApplyFocusMode()
    {
        if (_config.FocusMode == FocusMode.Click || _currentSeat is not { IsRemoved: false } seat)
        {
            return;
        }

        var under = seat.PointerFocus;
        if (ReferenceEquals(under, _lastPointerFocus))
        {
            return;
        }

        _lastPointerFocus = under;
        if (under is null || !_byWindow.TryGetValue(under, out var mw) || mw.Hidden)
        {
            return;
        }

        if (ReferenceEquals(_focusStack.Focused, mw))
        {
            return;
        }

        if (_config.RaiseOnFocus)
        {
            Focus(mw);
        }
        else
        {
            FocusOnly(mw, seat);
        }
    }

    private void FocusOnly(ManagedWindow mw, WmSeat seat)
    {
        _focusStack.Focused = mw;
        seat.FocusWindow(mw.Window);
    }

    private void OnPointerRequest(ManagedWindow mw, WmSeat seat, Edges? edges)
    {
        if (_drag is { Op.IsEnded: false })
        {
            return;
        }

        Focus(mw);
        if (edges is not { } resizeEdges)
        {
            StartDrag(mw, seat, Edges.None);
            return;
        }

        if (mw.IsFixedSize)
        {
            return;
        }

        if (resizeEdges == Edges.None)
        {
            var pointer = seat.PointerPosition;
            resizeEdges = (pointer.X < mw.X + (mw.Width / 2) ? Edges.Left : Edges.Right)
                | (pointer.Y < mw.Y + (mw.Height / 2) ? Edges.Top : Edges.Bottom);
        }

        StartDrag(mw, seat, resizeEdges);
    }

    private void OnDecorEntered(uint seatName, uint surfaceId, double x, double y)
    {
        _pointerBar = BarFor(surfaceId);
        _pointerOnCalendar = _calendar is { } calendar && calendar.SurfaceId == surfaceId;
        _pointerMenu = MenuFor(surfaceId);
        _pointerWallpaper = WallpaperFor(surfaceId);
        _pointerTab = _pointerBar is null && !_pointerOnCalendar && _pointerMenu is null
            && _pointerWallpaper is null
            ? TabWindowFor(surfaceId)
            : null;
        _tabX = x;
        _tabY = y;
        if (_pointerMenu is { } enteredMenu)
        {
            _pointerInput.SetShape(seatName, CursorShape.Default);
            if (enteredMenu.UpdateHover((int)x, (int)y))
            {
                _wm.RequestManage();
            }

            return;
        }

        if (_pointerOnCalendar)
        {
            _pointerInput.SetShape(seatName, CursorShape.Default);
            return;
        }

        if (_pointerBar is { } bar)
        {
            _pointerInput.SetShape(seatName, CursorShape.Default);
            var changed = bar.UpdateHover((int)x, (int)y);
            if (_config.AutoHide && bar.AutoHidden)
            {
                bar.AutoHidden = false;
                changed = true;
            }

            if (_config.AutoRaise && !bar.Raised)
            {
                bar.Raised = true;
                changed = true;
            }

            if (changed)
            {
                _wm.RequestManage();
            }

            return;
        }

        if (_pointerTab is not null)
        {
            UpdateHoverCursor(seatName, x, y);
        }
    }

    private void OnDecorLeft(uint seatName, uint surfaceId)
    {
        _ = seatName;
        if (_pointerOnCalendar && _calendar is { } calendar && calendar.SurfaceId == surfaceId)
        {
            _pointerOnCalendar = false;
            return;
        }

        if (_pointerMenu is { } leftMenu && leftMenu.SurfaceId == surfaceId)
        {
            _pointerMenu = null;
            return;
        }

        if (_pointerWallpaper is { } leftWallpaper && leftWallpaper.SurfaceId == surfaceId)
        {
            _pointerWallpaper = null;
            return;
        }

        if (_pointerBar is { } bar && bar.SurfaceId == surfaceId)
        {
            bar.ClearHover();
            if (_config.AutoHide && !bar.AutoHidden && _barDrag is null && PointerFarFromBar(bar))
            {
                bar.AutoHidden = true;
            }

            if (_config.AutoRaise && bar.Raised)
            {
                bar.Raised = false;
            }

            _pointerBar = null;
            _wm.RequestManage();
            return;
        }

        if (_pointerTab is { } mw && mw.Tab?.SurfaceId == surfaceId)
        {
            var changed = mw.TabPressed is not null || mw.TabLeftDown;
            mw.TabPressed = null;
            mw.TabLeftDown = false;
            _pointerTab = null;
            if (changed)
            {
                _wm.RequestManage();
            }
        }
    }

    private void OnDecorMotion(uint seatName, double x, double y)
    {
        _tabX = x;
        _tabY = y;
        if (_pointerMenu is { } motionMenu)
        {
            if (motionMenu.UpdateHover((int)x, (int)y))
            {
                RenderMenus();
            }

            return;
        }

        if (_barDrag is { } drag)
        {
            var frame = drag.Bar.FrameRect;
            var point = new Point(frame.X + (int)x, frame.Y + (int)y);
            var candidate = PlacementRegions.PlacementAt(
                drag.Bar.Output.Area, point, VerticalLayout.MenuBarHeight);
            if (candidate != drag.Candidate)
            {
                _barDrag = (drag.Bar, candidate);
                RenderPlacementOutline();
            }

            return;
        }

        if (_pointerBar is { } bar)
        {
            if (bar.UpdateHover((int)x, (int)y))
            {
                RenderDeskbars();
            }

            return;
        }

        if (_pointerTab is not null)
        {
            UpdateHoverCursor(seatName, x, y);
        }
    }

    private void OnDecorButton(uint seatName, uint button, bool isPressed)
    {
        _ = seatName;
        if (_pointerMenu is { } menu)
        {
            if (button != InputCodes.BtnLeft || isPressed)
            {
                return;
            }

            if (menu.ItemAt((int)_tabX, (int)_tabY) is { } index)
            {
                var item = menu.Items[index];
                if (item.Children is { } children)
                {
                    OpenSubmenu(menu, index, children);
                }
                else if (item is { Enabled: true, Activate: { } activate })
                {
                    _menuActions.Enqueue(activate);
                    CloseMenus();
                    _wm.RequestManage();
                }
            }

            return;
        }

        if (_pointerOnCalendar)
        {
            if (button == InputCodes.BtnLeft && !isPressed && _calendar is { } calendar
                && calendar.HandleClick((int)_tabX, (int)_tabY))
            {
                _wm.RequestManage();
            }

            return;
        }

        if (_pointerWallpaper is { } wallpaper)
        {
            if (isPressed)
            {
                return;
            }

            if (button == InputCodes.BtnRight)
            {
                OpenDesktopMenu(wallpaper);
            }
            else if (button == InputCodes.BtnLeft)
            {
                CloseMenus();
                _actions.Enqueue(WmAction.ClearFocus);
                _wm.RequestManage();
            }

            return;
        }

        if (_pointerBar is not null || _barDrag is not null)
        {
            var bar = _pointerBar ?? _barDrag!.Value.Bar;
            if (button == InputCodes.BtnRight)
            {
                if (!isPressed && bar.HitAt((int)_tabX, (int)_tabY) is Team rightTeam)
                {
                    OpenTeamMenu(bar, rightTeam);
                }

                return;
            }

            if (button != InputCodes.BtnLeft)
            {
                return;
            }

            if (isPressed)
            {
                if (bar.HitAt((int)_tabX, (int)_tabY) is "handle")
                {
                    _barDrag = (bar, null);
                    _wm.RequestManage();
                }

                return;
            }

            if (_menus.Count > 0 && !isPressed)
            {
                CloseMenus();
            }

            if (_barDrag is { } drag)
            {
                _barDrag = null;
                if (drag.Candidate is { } candidate && candidate != _config.Placement)
                {
                    _config.SavePlacement(candidate, _log);
                }

                _wm.RequestManage();
                return;
            }

            switch (bar.HitAt((int)_tabX, (int)_tabY))
            {
                case Team team:
                    _teamActivations.Enqueue(team);
                    _wm.RequestManage();
                    break;
                case DeskbarSurface.ArrowHit arrow:
                    _arrowToggles.Enqueue(arrow.Team);
                    _wm.RequestManage();
                    break;
                case WindowEntry window:
                    _windowActivations.Enqueue(window.Window);
                    _wm.RequestManage();
                    break;
                case ClockApplet:
                    ToggleCalendar(bar);
                    break;
                case WorkspacesApplet workspaces:
                    var local = new Point(
                        (int)_tabX - workspaces.LastRect.X,
                        (int)_tabY - workspaces.LastRect.Y);
                    if (workspaces.CellAt(local) is { } cell)
                    {
                        _workspaceSwitches.Enqueue((cell, false));
                        _wm.RequestManage();
                    }

                    break;
                case "leaf":
                    OpenLeafMenu(bar);
                    break;
            }

            return;
        }

        if (button != InputCodes.BtnLeft || _pointerTab is not { } mw)
        {
            return;
        }

        var hit = DecorationHitFor(mw, (int)_tabX, (int)_tabY);
        if (isPressed)
        {
            mw.TabLeftDown = true;
            mw.TabPressed = hit.Part is FramePart.CloseBox or FramePart.ZoomBox ? hit.Part : null;
            if (hit.Part == FramePart.Tab)
            {
                var now = Environment.TickCount64;
                if (_lastTabClick is { } last && ReferenceEquals(last.Window, mw)
                    && now - last.At <= DoubleClickMs)
                {
                    _lastTabClick = null;
                    mw.Events.Enqueue((WindowEventKind.Minimize, null));
                }
                else
                {
                    _lastTabClick = (mw, now);
                }
            }

            _wm.RequestManage();
            return;
        }

        var pressed = mw.TabPressed;
        mw.TabPressed = null;
        mw.TabLeftDown = false;
        if (pressed is not null && pressed == hit.Part)
        {
            switch (pressed)
            {
                case FramePart.CloseBox:
                    mw.Events.Enqueue((WindowEventKind.Close, null));
                    break;
                case FramePart.ZoomBox:
                    mw.Events.Enqueue((WindowEventKind.Maximize, null));
                    break;
            }
        }

        _wm.RequestManage();
    }

    private DecorationHit DecorationHitFor(ManagedWindow mw, int localX, int localY)
    {
        if (mw.Area is { Windows.Count: > 1 } area && ReferenceEquals(area.Front, mw)
            && localY >= 0 && localY < mw.Metrics.TabHeight)
        {
            var slot = TabStrip.SlotAt(
                mw.Metrics.FrameWidth, mw.Metrics.TabHeight, area.Windows.Count, localX, localY);
            if (slot < 0 || slot >= area.Windows.Count)
            {
                return DecorationHit.None;
            }

            if (!ReferenceEquals(area.Windows[slot], mw))
            {
                return DecorationHit.None;
            }

            var slotRect = TabStrip.Slot(
                mw.Metrics.FrameWidth, mw.Metrics.TabHeight, area.Windows.Count, slot);
            var point = new Point(localX, localY);
            if (TabStrip.CloseRect(slotRect, mw.Metrics).Contains(point))
            {
                return new DecorationHit(FramePart.CloseBox, Edges.None);
            }

            if (TabStrip.ZoomRect(slotRect, mw.Metrics).Contains(point))
            {
                return new DecorationHit(FramePart.ZoomBox, Edges.None);
            }

            return new DecorationHit(FramePart.Tab, Edges.None);
        }

        return mw.Metrics.HitTest(localX, localY, mw.Width, mw.Height);
    }

    private void ToggleCalendar(DeskbarSurface bar)
    {
        if (_calendar is not null)
        {
            _calendar.Dispose();
            _calendar = null;
            return;
        }

        if (_compositor is null || _shm is null || _layerShell is null || _scales is null
            || _scales.ProxyForName(bar.Output.WlOutputName) is not { } proxy)
        {
            return;
        }

        var frame = bar.FrameRect;
        var area = bar.Output.Area;
        var size = CalendarPopup.SurfaceSize;
        int x;
        int y;
        if (bar.Placement.Orientation == BarOrientation.Horizontal)
        {
            x = Math.Clamp(frame.Right - size.Width - 4 - area.X, 0, Math.Max(area.Width - size.Width, 0));
            y = bar.Placement.End == BarEnd.Top
                ? frame.Bottom + 2 - area.Y
                : Math.Max(frame.Y - size.Height - 2 - area.Y, 0);
        }
        else
        {
            x = bar.Placement.Side == BarSide.Left
                ? frame.Right + 2 - area.X
                : Math.Max(frame.X - size.Width - 2 - area.X, 0);
            y = Math.Clamp(VerticalLayout.MenuBarHeight, 0, Math.Max(area.Height - size.Height, 0));
        }

        _calendar = new CalendarPopup(_compositor, _shm, _layerShell, proxy, bar.Output, _wm, new Point(x, y));
        _wm.RequestManage();
    }

    private bool PointerFarFromBar(DeskbarSurface bar)
    {
        const int preventDistance = 80;
        if (_currentSeat is not { IsRemoved: false } seat)
        {
            return true;
        }

        var frame = bar.FrameRect;
        if (frame.IsEmpty)
        {
            return true;
        }

        var near = new Rect(
            frame.X - preventDistance,
            frame.Y - preventDistance,
            frame.Width + (2 * preventDistance),
            frame.Height + (2 * preventDistance));
        return !near.Contains(seat.PointerPosition);
    }

    private MenuSurface? MenuFor(uint surfaceId)
    {
        foreach (var menu in _menus)
        {
            if (menu.SurfaceId == surfaceId)
            {
                return menu;
            }
        }

        return null;
    }

    private DeskbarSurface? BarFor(uint surfaceId)
    {
        foreach (var bar in _deskbars.Values)
        {
            if (bar.SurfaceId == surfaceId)
            {
                return bar;
            }
        }

        return null;
    }

    private ManagedWindow? TabWindowFor(uint surfaceId)
    {
        foreach (var mw in _windows)
        {
            if (mw.Tab?.SurfaceId == surfaceId)
            {
                return mw;
            }
        }

        return null;
    }

    private void UpdateHoverCursor(uint seatName, double x, double y)
    {
        if (_pointerTab is not { } mw)
        {
            return;
        }

        var hit = mw.Metrics.HitTest((int)x, (int)y, mw.Width, mw.Height);
        var shape = hit.Part switch
        {
            FramePart.ResizeCorner => CursorShape.SeResize,
            FramePart.Border => hit.Edges switch
            {
                Edges.Left => CursorShape.WResize,
                Edges.Right => CursorShape.EResize,
                Edges.Top => CursorShape.NResize,
                Edges.Bottom => CursorShape.SResize,
                Edges.Left | Edges.Top => CursorShape.NwResize,
                Edges.Right | Edges.Top => CursorShape.NeResize,
                Edges.Left | Edges.Bottom => CursorShape.SwResize,
                Edges.Right | Edges.Bottom => CursorShape.SeResize,
                _ => CursorShape.Default,
            },
            _ => CursorShape.Default,
        };
        _pointerInput.SetShape(seatName, shape);
    }

    private void StartDrag(ManagedWindow mw, WmSeat seat, Edges edges, bool fromTab = false)
    {
        var op = seat.StartPointerOperation();
        _drag = new DragState(mw, op, edges, mw.X, mw.Y, mw.Width, mw.Height) { FromTab = fromTab };
        if (edges != Edges.None)
        {
            mw.Window.InformResizing(true);
        }
    }

    private void RaiseInStack(SatArea area, ManagedWindow window)
    {
        area.Front = window;
        Focus(window);
    }

    private void StartSlide(ManagedWindow mw, WmSeat seat)
    {
        var op = seat.StartPointerOperation();
        _drag = new DragState(mw, op, Edges.None, mw.X, mw.Y, mw.Width, mw.Height)
        {
            Slide = true,
            StartTabOffset = mw.Metrics.TabRect.X,
        };
    }

    private void ApplyDrag(bool allowResize)
    {
        if (_drag is not { Op.IsEnded: false } drag || drag.Window.Window.IsClosed)
        {
            return;
        }

        var delta = drag.Op.Delta;
        var mw = drag.Window;
        var metrics = mw.Metrics;
        if (drag.Slide)
        {
            var maxOffset = Math.Max(mw.Metrics.FrameWidth - mw.Metrics.TabRect.Width, 0);
            if (maxOffset > 0)
            {
                var offset = Math.Clamp(drag.StartTabOffset + delta.X, 0, maxOffset);
                mw.TabLocation = offset / (float)maxOffset;
            }

            return;
        }

        if (drag.Edges == Edges.None)
        {
            if (mw.Area is { } movingArea)
            {
                var desiredCellX = drag.StartX + delta.X - metrics.BorderWidth;
                var desiredCellY = drag.StartY + delta.Y - metrics.TabHeight - metrics.BorderWidth;
                _sat.Translate(
                    movingArea.Group,
                    desiredCellX - movingArea.Left.Position,
                    desiredCellY - movingArea.Top.Position);
                ApplyGroup(movingArea.Group, allowResize);
                return;
            }

            if (mw.Output is { IsRemoved: false } output)
            {
                var startFrame = mw.Ssd
                    ? new Rect(
                        drag.StartX - metrics.BorderWidth,
                        drag.StartY - metrics.BorderWidth - metrics.TabHeight,
                        metrics.FrameWidth,
                        metrics.FrameHeight)
                    : new Rect(drag.StartX, drag.StartY, mw.Width, mw.Height);
                drag.Magnet.AlterDeltaForSnap(
                    WmOutputPolicy.UsableArea(output), startFrame, ref delta, Environment.TickCount64);
            }

            mw.SetPosition(drag.StartX + delta.X, drag.StartY + delta.Y);
            RefreshDropCandidates(drag, mw, delta);
            return;
        }

        if (mw.Area is { } resizeArea)
        {
            if (!allowResize)
            {
                return;
            }

            const int minCell = 100;
            var group = resizeArea.Group;
            if ((drag.Edges & Edges.Left) != 0)
            {
                _sat.MoveTab(
                    group, resizeArea.Left,
                    drag.StartX - metrics.BorderWidth + delta.X, minCell);
            }
            else if ((drag.Edges & Edges.Right) != 0)
            {
                _sat.MoveTab(
                    group, resizeArea.Right,
                    drag.StartX + drag.StartWidth + metrics.BorderWidth + delta.X, minCell);
            }

            if ((drag.Edges & Edges.Top) != 0)
            {
                _sat.MoveTab(
                    group, resizeArea.Top,
                    drag.StartY - metrics.TabHeight - metrics.BorderWidth + delta.Y, minCell);
            }
            else if ((drag.Edges & Edges.Bottom) != 0)
            {
                _sat.MoveTab(
                    group, resizeArea.Bottom,
                    drag.StartY + drag.StartHeight + metrics.BorderWidth + delta.Y, minCell);
            }

            ApplyGroup(group, allowResize);
            return;
        }

        if (!allowResize)
        {
            return;
        }

        var minimum = mw.Window.SizeHint.Minimum;
        var minWidth = Math.Max(minimum.Width, 1);
        var minHeight = Math.Max(minimum.Height, 1);
        var newWidth = drag.StartWidth;
        var newHeight = drag.StartHeight;
        if ((drag.Edges & Edges.Left) != 0)
        {
            newWidth = Math.Max(drag.StartWidth - delta.X, minWidth);
        }
        else if ((drag.Edges & Edges.Right) != 0)
        {
            newWidth = Math.Max(drag.StartWidth + delta.X, minWidth);
        }

        if ((drag.Edges & Edges.Top) != 0)
        {
            newHeight = Math.Max(drag.StartHeight - delta.Y, minHeight);
        }
        else if ((drag.Edges & Edges.Bottom) != 0)
        {
            newHeight = Math.Max(drag.StartHeight + delta.Y, minHeight);
        }

        var anchorX = (drag.Edges & Edges.Left) != 0
            ? drag.StartX + (drag.StartWidth - mw.Width)
            : drag.StartX;
        var anchorY = (drag.Edges & Edges.Top) != 0
            ? drag.StartY + (drag.StartHeight - mw.Height)
            : drag.StartY;
        if (anchorX != mw.X || anchorY != mw.Y)
        {
            mw.SetPosition(anchorX, anchorY);
        }

        drag.Desired = new Size(newWidth, newHeight);
        ProposePaced(drag, mw, drag.Desired);
    }

    private static void ProposePaced(DragState drag, ManagedWindow mw, Size desired)
    {
        if (drag.LastProposed == desired)
        {
            return;
        }

        drag.LastProposed = desired;
        mw.Window.ProposeDimensions(desired.Width, desired.Height);
    }

    private void RefreshDropCandidates(DragState drag, ManagedWindow mw, Point delta)
    {
        drag.StackTarget = null;
        drag.TileTarget = null;
        if (!_config.StackAndTile || _currentSeat is not { IsRemoved: false } seat)
        {
            return;
        }

        var pointer = seat.PointerPosition;
        if (drag.FromTab)
        {
            foreach (var other in _windows)
            {
                if (ReferenceEquals(other, mw) || other.Hidden || !other.Ssd
                    || !OnCurrentWorkspace(other)
                    || (other.Area is { } area && !ReferenceEquals(area.Front, other)))
                {
                    continue;
                }

                var frame = other.FrameRect;
                var m = other.Metrics;
                var tabRect = other.Area is { Windows.Count: > 1 }
                    ? new Rect(frame.X, frame.Y, m.FrameWidth, m.TabHeight)
                    : new Rect(frame.X + m.TabRect.X, frame.Y, m.TabRect.Width, m.TabRect.Height);
                var region = new Rect(
                    tabRect.X - m.TabHeight,
                    tabRect.Y - m.TabHeight,
                    tabRect.Width + (2 * m.TabHeight),
                    tabRect.Height + (2 * m.TabHeight));
                if (region.Contains(pointer))
                {
                    drag.StackTarget = new StackDrop(other, tabRect);
                    return;
                }
            }
        }

        if (!CtrlHeld)
        {
            return;
        }

        var movingFrame = mw.FrameRect;
        var snap = _config.SnapDistance;
        TileDrop? best = null;
        var bestDistance = snap + 1;
        foreach (var other in _windows)
        {
            if (ReferenceEquals(other, mw) || other.Hidden || !other.Ssd || !OnCurrentWorkspace(other)
                || ReferenceEquals(other.Area, mw.Area) && mw.Area is not null
                || (other.Area is { } area && !ReferenceEquals(area.Front, other)))
            {
                continue;
            }

            var frame = other.FrameRect;
            var verticalOverlap = Math.Min(movingFrame.Bottom, frame.Bottom)
                - Math.Max(movingFrame.Y, frame.Y);
            var horizontalOverlap = Math.Min(movingFrame.Right, frame.Right)
                - Math.Max(movingFrame.X, frame.X);

            if (verticalOverlap > 0)
            {
                Consider(Edges.Left, Math.Abs(movingFrame.X - frame.Right), frame.Right,
                    new Rect(frame.Right - 3, frame.Y, 6, frame.Height), other);
                Consider(Edges.Right, Math.Abs(movingFrame.Right - frame.X), frame.X,
                    new Rect(frame.X - 3, frame.Y, 6, frame.Height), other);
            }

            if (horizontalOverlap > 0)
            {
                Consider(Edges.Top, Math.Abs(movingFrame.Y - frame.Bottom), frame.Bottom,
                    new Rect(frame.X, frame.Bottom - 3, frame.Width, 6), other);
                Consider(Edges.Bottom, Math.Abs(movingFrame.Bottom - frame.Y), frame.Y,
                    new Rect(frame.X, frame.Y - 3, frame.Width, 6), other);
            }
        }

        drag.TileTarget = best;

        void Consider(Edges edge, int distance, int position, Rect region, ManagedWindow target)
        {
            if (distance <= snap && distance < bestDistance)
            {
                bestDistance = distance;
                best = new TileDrop(target, edge, position, region);
            }
        }
    }

    private bool CtrlHeld => _currentSeat is { } seat && _ctrlHeld.GetValueOrDefault(seat);

    private void ApplyGroup(SatGroup group, bool allowResize)
    {
        foreach (var area in group.Areas)
        {
            var cell = area.Cell;
            foreach (var window in area.Windows)
            {
                if (window.Window.IsClosed)
                {
                    continue;
                }

                var m = window.Metrics;
                var x = cell.X + m.BorderWidth;
                var y = cell.Y + m.TabHeight + m.BorderWidth;
                if (window.X != x || window.Y != y || window.PositionUndefined)
                {
                    window.SetPosition(x, y);
                }

                if (allowResize)
                {
                    var desired = new Size(
                        Math.Max(cell.Width - (2 * m.BorderWidth), 1),
                        Math.Max(cell.Height - m.TabHeight - (2 * m.BorderWidth), 1));
                    if (window.ProposedCell != desired)
                    {
                        window.ProposedCell = desired;
                        window.Propose(desired.Width, desired.Height);
                    }
                }
            }
        }
    }

    private void ApplyGroups(bool allowResize)
    {
        foreach (var group in _sat.Groups)
        {
            ApplyGroup(group, allowResize);
        }
    }

    private void FinishReleasedDrag()
    {
        if (_drag is not { } drag || drag.Op.IsEnded || !drag.Op.IsReleased)
        {
            return;
        }

        _drag = null;
        if (drag.Edges != Edges.None && !drag.Window.Window.IsClosed)
        {
            drag.Window.Window.InformResizing(false);
            if (drag.Window.Area is null && !drag.Desired.IsEmpty && drag.LastProposed != drag.Desired)
            {
                drag.Window.Window.ProposeDimensions(drag.Desired.Width, drag.Desired.Height);
            }
        }

        drag.Op.End();

        var mw = drag.Window;
        if (!mw.Window.IsClosed && _config.StackAndTile)
        {
            if (drag.StackTarget is { } stack && !stack.Target.Window.IsClosed)
            {
                _sat.Stack(mw, stack.Target);
                if (mw.Area is { } stackedArea)
                {
                    ApplyGroup(stackedArea.Group, allowResize: true);
                }

                Focus(mw);
            }
            else if (drag.TileTarget is { } tile && !tile.Target.Window.IsClosed)
            {
                _sat.TileLink(mw, tile.Target, tile.MovingEdge, mw.Metrics.TabHeight);
                if (mw.Area is { } tiledArea)
                {
                    ApplyGroup(tiledArea.Group, allowResize: true);
                }
            }
        }

        UpdateOutputFromRect(drag.Window);
    }

    private void StartPointerMove()
    {
        if (_currentSeat is not { IsRemoved: false } seat)
        {
            return;
        }

        if (seat.PointerFocus is not { } under || !_byWindow.TryGetValue(under, out var mw))
        {
            return;
        }

        Focus(mw);
        StartDrag(mw, seat, Edges.None);
    }

    private void StartPointerResize()
    {
        if (_currentSeat is not { IsRemoved: false } seat)
        {
            return;
        }

        if (seat.PointerFocus is not { } under || !_byWindow.TryGetValue(under, out var mw)
            || mw.IsFixedSize)
        {
            return;
        }

        Focus(mw);
        var pointer = seat.PointerPosition;
        var edges = (pointer.X < mw.X + (mw.Width / 2) ? Edges.Left : Edges.Right)
            | (pointer.Y < mw.Y + (mw.Height / 2) ? Edges.Top : Edges.Bottom);
        StartDrag(mw, seat, edges);
    }

    private void DrainWorkspaceSwitches()
    {
        while (_workspaceSwitches.TryDequeue(out var request))
        {
            SwitchWorkspace(request.Index, request.TakeWindow);
        }

        while (_hotkeyPresses.TryDequeue(out var hotkey))
        {
            ExecuteHotkey(hotkey);
        }
    }

    private void ExecuteHotkey(Hotkey hotkey)
    {
        if (hotkey.Command is { } command)
        {
            Spawn(command);
            return;
        }

        switch (hotkey.Action)
        {
            case "close":
                _focusStack.Focused?.Window.Close();
                break;
            case "zoom":
                if (_focusStack.Focused is { } toZoom)
                {
                    ToggleZoom(toZoom);
                }

                break;
            case "minimize":
                if (_focusStack.Focused is { } toHide)
                {
                    Hide(toHide);
                }

                break;
            case "terminal":
                Spawn(_config.TerminalCommand);
                break;
            case "previous-workspace":
                SwitchWorkspace(_workspaces.Previous, takeWindow: false);
                break;
            case { } action when action.StartsWith("workspace ", StringComparison.Ordinal)
                && int.TryParse(action[10..], out var index):
                SwitchWorkspace(index - 1, takeWindow: false);
                break;
        }
    }

    private void DrainActions()
    {
        while (_actions.TryDequeue(out var action))
        {
            Execute(action);
        }
    }

    private void Execute(WmAction action)
    {
        switch (action)
        {
            case WmAction.Close:
                _focusStack.Focused?.Window.Close();
                break;
            case WmAction.MinimizeFocused:
                if (_focusStack.Focused is { } toHide)
                {
                    Hide(toHide);
                }

                break;
            case WmAction.SpawnTerminal:
                Spawn(_config.TerminalCommand);
                break;
            case WmAction.ClearFocus:
                _focusStack.ClearFocus();
                break;
            case WmAction.RestoreFocus:
                _focusStack.RestoreFromStack(static mw => !mw.Hidden);
                break;
            case WmAction.PointerMove:
                StartPointerMove();
                break;
            case WmAction.PointerResize:
                StartPointerResize();
                break;
            case WmAction.SwitcherNextTeam:
                SwitcherStep(teamDirection: 1, windowDirection: 0);
                break;
            case WmAction.SwitcherPreviousTeam:
                SwitcherStep(teamDirection: -1, windowDirection: 0);
                break;
            case WmAction.SwitcherNextWindow:
                SwitcherStep(teamDirection: 0, windowDirection: 1);
                break;
            case WmAction.SwitcherPreviousWindow:
                SwitcherStep(teamDirection: 0, windowDirection: -1);
                break;
            case WmAction.SwitcherCommit:
                EndSwitcher(commit: true);
                break;
            case WmAction.SwitcherCancel:
                EndSwitcher(commit: false);
                break;
            case WmAction.ZoomFocused:
                if (_focusStack.Focused is { } toZoom)
                {
                    ToggleZoom(toZoom);
                }

                break;
        }
    }

    private void SwitcherStep(int teamDirection, int windowDirection)
    {
        if (_switcher is null)
        {
            StartSwitcher();
            if (_switcher is null)
            {
                return;
            }

            if (teamDirection != 0)
            {
                _switcher.CycleTeam(teamDirection);
            }

            return;
        }

        _switcher.Prune();
        if (teamDirection != 0)
        {
            _switcher.CycleTeam(teamDirection);
        }

        if (windowDirection != 0)
        {
            _switcher.CycleWindow(windowDirection);
        }
    }

    private void StartSwitcher()
    {
        if (_currentSeat is not { IsRemoved: false } seat || !_wm.Bindings.IsSupported
            || _wm.Bindings.Version < 2)
        {
            return;
        }

        var teams = _teams.Teams(_config.SortTeams);
        if (teams.Count == 0)
        {
            return;
        }

        var currentTeam = _focusStack.Focused is { } focused ? _teams.TeamOf(focused) : null;
        _switcher = new SwitcherState(teams, currentTeam);

        if (!_switcherArmed)
        {
            _switcherArmed = true;
            _switcherBindings.Add(_wm.Bindings.Bind(
                seat, "Tab", Modifiers.Ctrl, () => Enqueue(WmAction.SwitcherNextTeam)));
            _switcherBindings.Add(_wm.Bindings.Bind(
                seat, "Tab", Modifiers.Ctrl | Modifiers.Shift, () => Enqueue(WmAction.SwitcherPreviousTeam)));
            _switcherBindings.Add(_wm.Bindings.Bind(
                seat, "grave", Modifiers.Ctrl, () => Enqueue(WmAction.SwitcherNextWindow)));
            _switcherBindings.Add(_wm.Bindings.Bind(
                seat, "Right", Modifiers.Ctrl, () => Enqueue(WmAction.SwitcherNextTeam)));
            _switcherBindings.Add(_wm.Bindings.Bind(
                seat, "Left", Modifiers.Ctrl, () => Enqueue(WmAction.SwitcherPreviousTeam)));
            _switcherBindings.Add(_wm.Bindings.Bind(
                seat, "Down", Modifiers.Ctrl, () => Enqueue(WmAction.SwitcherNextWindow)));
            _switcherBindings.Add(_wm.Bindings.Bind(
                seat, "Up", Modifiers.Ctrl, () => Enqueue(WmAction.SwitcherPreviousWindow)));
            _switcherBindings.Add(_wm.Bindings.Bind(
                seat, "Escape", Modifiers.Ctrl, () => Enqueue(WmAction.SwitcherCancel)));
        }

        _switcherSubmap = _wm.Bindings.EnterSubmap(seat, _switcherBindings, TimeSpan.Zero);
    }

    private void Enqueue(WmAction action)
    {
        _actions.Enqueue(action);
        _wm.RequestManage();
    }

    private void EndSwitcher(bool commit)
    {
        if (_switcher is not { } state)
        {
            return;
        }

        _switcher = null;
        _switcherSubmap?.Exit();
        _switcherSubmap = null;
        _switcherSurface?.Dispose();
        _switcherSurface = null;

        if (!commit)
        {
            return;
        }

        state.Prune();
        if (state.SelectedTeam is not { } team || state.SelectedWindow is not { } window)
        {
            return;
        }

        for (var i = team.Windows.Count - 1; i >= 0; i--)
        {
            var mw = team.Windows[i];
            if (!ReferenceEquals(mw, window) && !mw.Window.IsClosed)
            {
                Focus(mw);
            }
        }

        if (!window.Window.IsClosed)
        {
            Focus(window);
        }
    }

    private void RenderDropHighlight()
    {
        Rect region = default;
        WmOutput? output = null;
        if (_drag is { Op.IsEnded: false } drag)
        {
            if (drag.StackTarget is { } stack)
            {
                region = stack.Region;
                output = drag.Window.Output;
            }
            else if (drag.TileTarget is { } tile)
            {
                region = tile.Region;
                output = drag.Window.Output;
            }
        }

        if (output is null || region.IsEmpty)
        {
            if (_dropHighlight is not null)
            {
                _dropHighlight.Dispose();
                _dropHighlight = null;
            }

            return;
        }

        if (_dropHighlight is not null && !ReferenceEquals(_dropHighlight.Output, output))
        {
            _dropHighlight.Dispose();
            _dropHighlight = null;
        }

        if (_dropHighlight is null)
        {
            if (_compositor is null || _shm is null || _layerShell is null || _scales is null
                || _scales.ProxyForName(output.WlOutputName) is not { } proxy)
            {
                return;
            }

            _dropHighlight = new DropHighlight(_compositor, _shm, _layerShell, proxy, output, _wm);
        }

        var scale = _scales?.ScaleForName(output.WlOutputName) ?? 1;
        if (_dropHighlight.Render(region, scale))
        {
            _dropHighlight.Commit();
        }
    }

    private void RenderSwitcher()
    {
        if (_switcher is not { } state)
        {
            return;
        }

        state.Prune();
        if (state.Teams.Count == 0)
        {
            EndSwitcher(commit: false);
            return;
        }

        if (_switcherSurface is null)
        {
            if (_compositor is null || _shm is null || _layerShell is null || _scales is null
                || _currentOutput is not { IsRemoved: false } output
                || _scales.ProxyForName(output.WlOutputName) is not { } proxy)
            {
                return;
            }

            _switcherSurface = new SwitcherSurface(_compositor, _shm, _layerShell, proxy, output, _wm);
        }

        var scale = _scales?.ScaleForName(_switcherSurface.Output.WlOutputName) ?? 1;
        if (_switcherSurface.Render(state, _icons, scale))
        {
            _switcherSurface.Commit();
        }
    }

    private void DrainWindowEvents()
    {
        foreach (var mw in _windows)
        {
            while (mw.Events.TryDequeue(out var entry))
            {
                HandleWindowEvent(mw, entry.Kind, entry.Output);
            }
        }
    }

    private void HandleWindowEvent(ManagedWindow mw, WindowEventKind kind, WmOutput? output)
    {
        if (mw.Window.IsClosed)
        {
            return;
        }

        switch (kind)
        {
            case WindowEventKind.Init:
                mw.WorkspaceMask = Workspace.MaskOf(_workspaces.Current);
                foreach (var rule in _config.Rules)
                {
                    if (!rule.MatchesText(mw.Window.AppId, mw.Window.Title))
                    {
                        continue;
                    }

                    if (rule.AllWorkspaces)
                    {
                        mw.WorkspaceMask = Workspace.AllMask;
                    }
                    else if (rule.Workspace is { } assigned && assigned >= 1)
                    {
                        mw.WorkspaceMask = Workspace.MaskOf(assigned - 1);
                    }

                    break;
                }

                mw.ProposePreferred();
                if (OnCurrentWorkspace(mw))
                {
                    Focus(mw);
                }

                break;
            case WindowEventKind.Close:
                mw.Window.Close();
                break;
            case WindowEventKind.Fullscreen:
                var target = output is { IsRemoved: false } ? output : mw.Output ?? _currentOutput;
                if (target is { IsRemoved: false })
                {
                    EnterFullscreen(mw, target);
                }

                break;
            case WindowEventKind.Unfullscreen:
                if (mw.FullscreenOutput is not null)
                {
                    ExitFullscreen(mw);
                }

                break;
            case WindowEventKind.Minimize:
                Hide(mw);
                break;
            case WindowEventKind.Maximize:
                ToggleZoom(mw);
                break;
            case WindowEventKind.Unmaximize:
                if (mw.Zoom is not null)
                {
                    Unzoom(mw);
                }

                break;
        }
    }

    private bool OnCurrentWorkspace(ManagedWindow mw) =>
        mw.WorkspaceMask == 0 || Workspace.Includes(mw.WorkspaceMask, _workspaces.Current);

    private void SwitchWorkspace(int index, bool takeWindow)
    {
        if (takeWindow && _focusStack.Focused is { } taken && taken.WorkspaceMask != Workspace.AllMask)
        {
            if (taken.Area is { } takenArea)
            {
                foreach (var area in takenArea.Group.Areas)
                {
                    foreach (var member in area.Windows)
                    {
                        member.WorkspaceMask = Workspace.MaskOf(index);
                    }
                }
            }
            else
            {
                taken.WorkspaceMask = Workspace.MaskOf(index);
            }
        }

        if (!_workspaces.SwitchTo(index))
        {
            return;
        }

        if (_focusStack.Focused is { } focused && !OnCurrentWorkspace(focused))
        {
            if (!_focusStack.RestoreFromStack(mw => !mw.Hidden && OnCurrentWorkspace(mw)))
            {
                _focusStack.ClearFocus();
            }
        }
    }

    private void Focus(ManagedWindow mw)
    {
        if (mw.Hidden)
        {
            mw.Hidden = false;
        }

        if (mw.Area is { } area)
        {
            area.Front = mw;
        }

        if (!OnCurrentWorkspace(mw))
        {
            SwitchWorkspace(FirstWorkspaceOf(mw), takeWindow: false);
        }

        _focusStack.Focus(mw);
        if (_config.FocusMode == FocusMode.FollowMouseWarping
            && _currentSeat is { IsRemoved: false } seat
            && !mw.ContentRect.Contains(seat.PointerPosition)
            && _wm.Version >= 3
            && mw.Width > 0)
        {
            seat.WarpPointer(mw.X + (mw.Width / 2), mw.Y + (mw.Height / 2));
        }
    }

    private static int FirstWorkspaceOf(ManagedWindow mw)
    {
        for (var i = 0; i < 32; i++)
        {
            if (Workspace.Includes(mw.WorkspaceMask, i))
            {
                return i;
            }
        }

        return 0;
    }

    private void Hide(ManagedWindow mw)
    {
        if (mw.Area is { } area)
        {
            foreach (var member in area.Group.Areas.SelectMany(static a => a.Windows))
            {
                member.Hidden = true;
            }
        }
        else
        {
            mw.Hidden = true;
        }

        if (ReferenceEquals(_focusStack.Focused, mw))
        {
            if (!_focusStack.RestoreFromStack(other => !other.Hidden && OnCurrentWorkspace(other)))
            {
                _focusStack.ClearFocus();
            }
        }
    }

    private void EnterFullscreen(ManagedWindow mw, WmOutput output)
    {
        if (mw.Width > 0)
        {
            mw.PreFullscreen ??= mw.ContentRect;
        }

        mw.Window.Fullscreen(output);
        mw.Window.InformFullscreen(true);
        mw.FullscreenOutput = output;
    }

    private static void ExitFullscreen(ManagedWindow mw)
    {
        mw.Window.ExitFullscreen();
        mw.Window.InformFullscreen(false);
        mw.FullscreenOutput = null;

        if (mw.PreFullscreen is { Width: > 0, Height: > 0 } saved)
        {
            mw.SetPosition(saved.X, saved.Y);
            mw.Propose(saved.Width, saved.Height);
        }
    }

    private void ToggleZoom(ManagedWindow mw)
    {
        if (mw.FullscreenOutput is not null || mw.IsFixedSize)
        {
            return;
        }

        if ((mw.Output ?? _currentOutput) is not { IsRemoved: false } output)
        {
            return;
        }

        var shift = _currentSeat is { } seat && ShiftHeld(seat);
        var area = shift ? output.Area : WmOutputPolicy.UsableArea(output);
        if (!shift && _deskbars.TryGetValue(output, out var bar)
            && bar.Placement.Orientation == BarOrientation.Vertical)
        {
            var frame = bar.FrameRect;
            if (!frame.IsEmpty && frame.Intersect(area) is { IsEmpty: false })
            {
                area = bar.Placement.Side == BarSide.Right
                    ? area with { Width = Math.Max(frame.X - 2 - area.X, 1) }
                    : new Rect(frame.Right + 2, area.Y, Math.Max(area.Right - frame.Right - 2, 1), area.Height);
            }
        }

        var m = mw.Ssd ? mw.Metrics : default;
        var innerWidth = area.Width - (2 * m.BorderWidth);
        var innerHeight = area.Height - m.TabHeight - (2 * m.BorderWidth);
        var maxWidth = innerWidth;
        var maxHeight = innerHeight;
        var hintMax = mw.Window.SizeHint.Maximum;
        if (hintMax.Width > 0)
        {
            maxWidth = Math.Min(maxWidth, hintMax.Width);
        }

        if (hintMax.Height > 0)
        {
            maxHeight = Math.Min(maxHeight, hintMax.Height);
        }

        maxWidth = Math.Max(maxWidth, 1);
        maxHeight = Math.Max(maxHeight, 1);

        if (mw.Zoom is not null && mw.Width == maxWidth && mw.Height == maxHeight)
        {
            Unzoom(mw);
            return;
        }

        mw.Zoom ??= new ZoomState(mw.ContentRect);
        var x = area.X + m.BorderWidth + Math.Max((innerWidth - maxWidth) / 2, 0);
        var y = area.Y + m.TabHeight + m.BorderWidth + Math.Max((innerHeight - maxHeight) / 2, 0);
        mw.SetPosition(x, y);
        mw.Propose(maxWidth, maxHeight);
        mw.Window.InformMaximized(true);
    }

    private static void Unzoom(ManagedWindow mw)
    {
        if (mw.Zoom is not { } zoom)
        {
            return;
        }

        mw.Zoom = null;
        mw.SetPosition(zoom.Restore.X, zoom.Restore.Y);
        mw.Propose(zoom.Restore.Width, zoom.Restore.Height);
        mw.Window.InformMaximized(false);
    }

    private void ApplyInitialPositions()
    {
        if (_windows.Count == 0)
        {
            _cascadeSteps.Clear();
        }

        foreach (var mw in _windows)
        {
            if (!mw.PositionUndefined || mw.Width <= 0 || mw.Height <= 0)
            {
                continue;
            }

            if (ParentOf(mw) is not { PositionUndefined: false, Width: > 0, Height: > 0 } parent)
            {
                continue;
            }

            var cx = parent.X + ((parent.Width - mw.Width) / 2);
            var cy = parent.Y + ((parent.Height - mw.Height) / 2);
            mw.Output = parent.Output;
            mw.SetPosition(cx, cy);
        }

        foreach (var output in _outputs)
        {
            _cascade.Clear();
            foreach (var mw in _windows)
            {
                if (!mw.PositionUndefined || mw.Width <= 0 || mw.Height <= 0)
                {
                    continue;
                }

                var target = mw.Output ?? _currentOutput;
                if (ReferenceEquals(target, output))
                {
                    _cascade.Add(mw);
                }
            }

            if (_cascade.Count == 0)
            {
                continue;
            }

            var area = WmOutputPolicy.UsableArea(output);
            if (area.IsEmpty)
            {
                continue;
            }

            var step = _cascadeSteps.GetValueOrDefault(output);
            var bar = _deskbars.GetValueOrDefault(output);
            foreach (var mw in _cascade)
            {
                var inset = mw.Ssd ? mw.Metrics.TabHeight + mw.Metrics.BorderWidth : 0;
                var offset = CascadePadding * (step % 8);
                var x = area.X + CascadePadding + offset;
                var y = area.Y + CascadePadding + inset + offset;
                if (bar is not null)
                {
                    (x, y) = AvoidDeskbar(bar, x, y, mw.Width, mw.Height);
                }

                x = Math.Min(x, Math.Max(area.Right - mw.Width, area.X));
                y = Math.Min(y, Math.Max(area.Bottom - mw.Height, area.Y));
                mw.Output = output;
                mw.SetPosition(x, y);
                step++;
            }

            _cascadeSteps[output] = step;
        }
    }

    private static (int X, int Y) AvoidDeskbar(DeskbarSurface bar, int x, int y, int width, int height)
    {
        var frame = bar.FrameRect;
        if (frame.IsEmpty || frame.Intersect(new Rect(x, y, Math.Max(width, 1), Math.Max(height, 1))).IsEmpty)
        {
            return (x, y);
        }

        if (bar.Placement.Orientation == BarOrientation.Vertical)
        {
            return bar.Placement.Side == BarSide.Left
                ? (Math.Max(x, frame.Right + CascadePadding), y)
                : (Math.Min(x, frame.X - width - CascadePadding), y);
        }

        return bar.Placement.End == BarEnd.Top
            ? (x, Math.Max(y, frame.Bottom + CascadePadding))
            : (x, Math.Min(y, frame.Y - height - CascadePadding));
    }

    private ManagedWindow? ParentOf(ManagedWindow mw) =>
        mw.Window.Parent is { } parent ? _byWindow.GetValueOrDefault(parent) : null;

    private void Restack()
    {
        for (var i = _focusStack.Count - 1; i >= 0; i--)
        {
            var mw = _focusStack[i];
            if (!mw.Window.IsClosed)
            {
                mw.Window.Node.PlaceTop();
            }
        }

        foreach (var mw in _windows)
        {
            if (mw.Window.IsClosed || mw.Feel == WindowFeel.Normal)
            {
                continue;
            }

            if (ParentOf(mw) is { } parent && !parent.Window.IsClosed)
            {
                mw.Window.Node.PlaceAbove(parent.Window.Node);
            }
        }
    }

    private void ReassociateOutputs()
    {
        foreach (var mw in _windows)
        {
            if (mw.Output is { IsRemoved: true })
            {
                mw.Output = _currentOutput;
            }
        }
    }

    private void UpdateOutputFromRect(ManagedWindow mw)
    {
        WmOutput? best = null;
        long bestArea = 0;
        foreach (var output in _outputs)
        {
            var overlap = output.Area.Intersect(mw.ContentRect);
            long area = (long)overlap.Width * overlap.Height;
            if (!overlap.IsEmpty && area > bestArea)
            {
                best = output;
                bestArea = area;
            }
        }

        if (best is not null)
        {
            mw.Output = best;
        }
    }

    private void Spawn(string[] command)
    {
        if (WmSpawn.Run(command) is { } failure)
        {
            _log.Error($"spawn failed: {failure}");
        }
    }

    private void OnReload()
    {
        _config = Config.Load(_noConfig, _log);
        _log.Info($"configuration reloaded");

        foreach (var binding in _bindings)
        {
            binding.Destroy();
        }

        _bindings.Clear();
        _armedSeats.Clear();
        _font?.Dispose();
        _font = null;

        foreach (var mw in _windows)
        {
            mw.Tab?.Invalidate();
        }

        foreach (var bar in _deskbars.Values)
        {
            bar.Invalidate();
        }

        _workspaces.Configure(_config.WorkspaceRows, _config.WorkspaceColumns);
        _icons.Reconfigure(_config);
        _clock.Reconfigure(_config);
        _clockTimer?.UpdateTimer(ClockIntervalMs());
        _calendar?.Dispose();
        _calendar = null;
        foreach (var wallpaper in _wallpapers.Values)
        {
            wallpaper.Invalidate();
        }

        CloseMenus();
    }

    internal Config Configuration => _config;

    internal BasinLogger Logger => _log;

    internal RecentItems Recents => _recents;

    internal WorkspaceGrid Workspaces => _workspaces;

    internal void SwitchWorkspaceFromMenu(int index) => SwitchWorkspace(index, takeWindow: false);

    internal void LaunchApp(AppEntry app)
    {
        var argv = DesktopEntries.SplitExec(app.Exec);
        if (argv.Length == 0)
        {
            return;
        }

        Spawn(argv);
        _recents.RecordLaunch(app.Id, _config.RecentApplicationsCount);
    }

    internal void OpenPath(string path) => Spawn(["xdg-open", path]);

    internal void SetPlacement(DeskbarPlacement placement) =>
        _config.SavePlacement(placement.Normalize(out _), _log);

    internal void SetDeskbarFlag(string key, bool value)
    {
        _config.SaveKey("deskbar", key, value ? "true" : "false", _log);
        InvalidateBars();
    }

    internal void SetClockFlag(string key, bool value)
    {
        _config.SaveKey("deskbar.clock", key, value ? "true" : "false", _log);
        _clock.Reconfigure(_config);
        _clockTimer?.UpdateTimer(ClockIntervalMs());
        InvalidateBars();
    }

    internal void SetIconSize(int size)
    {
        _config.IconSize = size;
        _config.SaveKey("deskbar", "icon-size", size.ToString(), _log);
        InvalidateBars();
    }

    internal void ActivateWindow(ManagedWindow mw)
    {
        if (!mw.Window.IsClosed)
        {
            Focus(mw);
        }
    }

    internal void HideTeam(Team team)
    {
        foreach (var mw in team.Windows)
        {
            mw.Hidden = true;
        }

        if (_focusStack.Focused is { Hidden: true })
        {
            if (!_focusStack.RestoreFromStack(other => !other.Hidden && OnCurrentWorkspace(other)))
            {
                _focusStack.ClearFocus();
            }
        }
    }

    internal void CloseTeam(Team team)
    {
        foreach (var mw in team.Windows)
        {
            if (!mw.Window.IsClosed)
            {
                mw.Window.Close();
            }
        }
    }

    private void InvalidateBars()
    {
        foreach (var bar in _deskbars.Values)
        {
            bar.Invalidate();
        }
    }

    private void OpenLeafMenu(DeskbarSurface bar)
    {
        CloseMenus();
        var frame = bar.FrameRect;
        var area = bar.Output.Area;
        var placement = bar.Placement;
        var mini = placement.State == DeskbarState.Mini;
        var horizontal = placement.Orientation == BarOrientation.Horizontal;

        bool alignRight;
        bool alignBottom;
        Point origin;
        if (!horizontal && !mini)
        {
            alignRight = placement.Side == BarSide.Right;
            alignBottom = false;
            origin = new Point(
                alignRight ? frame.X - 2 - area.X : frame.Right + 2 - area.X,
                frame.Y - area.Y);
        }
        else
        {
            alignRight = placement.Side == BarSide.Right && (mini || !horizontal);
            alignBottom = placement.End == BarEnd.Bottom;
            origin = new Point(
                alignRight ? frame.Right - area.X : frame.X - area.X,
                alignBottom ? frame.Y - 2 - area.Y : frame.Bottom + 2 - area.Y);
        }

        OpenMenu(bar.Output, LeafMenu.Build(this), origin, alignRight, alignBottom);
    }

    private WallpaperSurface? WallpaperFor(uint surfaceId)
    {
        foreach (var wallpaper in _wallpapers.Values)
        {
            if (wallpaper.SurfaceId == surfaceId)
            {
                return wallpaper;
            }
        }

        return null;
    }

    private void OpenDesktopMenu(WallpaperSurface wallpaper)
    {
        CloseMenus();
        OpenMenu(
            wallpaper.Output,
            DesktopMenu.Build(this),
            new Point((int)_tabX, (int)_tabY),
            alignRight: false,
            alignBottom: false);
    }

    private void OpenTeamMenu(DeskbarSurface bar, Team team)
    {
        CloseMenus();
        var frame = bar.FrameRect;
        var area = bar.Output.Area;
        OpenMenu(
            bar.Output,
            TeamMenu.Build(this, team),
            new Point(frame.X + (int)_tabX - area.X, frame.Y + (int)_tabY - area.Y),
            alignRight: false,
            alignBottom: false);
    }

    private void OpenMenu(
        WmOutput output,
        IReadOnlyList<MenuItemEntry> items,
        Point origin,
        bool alignRight,
        bool alignBottom)
    {
        if (_compositor is null || _shm is null || _layerShell is null || _scales is null
            || _scales.ProxyForName(output.WlOutputName) is not { } proxy)
        {
            return;
        }

        _menus.Add(new MenuSurface(
            _compositor, _shm, _layerShell, proxy, output, _wm, items, origin, alignRight, alignBottom));
        _wm.RequestManage();
    }

    private void OpenSubmenu(MenuSurface parent, int index, IReadOnlyList<MenuItemEntry> items)
    {
        var depth = _menus.IndexOf(parent);
        for (var i = _menus.Count - 1; i > depth; i--)
        {
            _menus[i].Dispose();
            _menus.RemoveAt(i);
        }

        var rect = parent.ItemRect(index);
        var area = parent.Output.Area;
        var openLeft = parent.Origin.X + parent.SurfaceSize.Width + 200 > area.Width;
        OpenMenu(
            parent.Output,
            items,
            openLeft
                ? new Point(parent.Origin.X + 3, parent.Origin.Y + rect.Y)
                : new Point(parent.Origin.X + parent.SurfaceSize.Width - 3, parent.Origin.Y + rect.Y),
            alignRight: openLeft,
            alignBottom: false);
    }

    private void CloseMenus()
    {
        if (_menus.Count == 0)
        {
            return;
        }

        foreach (var menu in _menus)
        {
            menu.Dispose();
        }

        _menus.Clear();
        _pointerMenu = null;
        _wm.RequestManage();
    }

    private void RenderMenus()
    {
        foreach (var menu in _menus)
        {
            var scale = _scales?.ScaleForName(menu.Output.WlOutputName) ?? 1;
            if (menu.Render(scale))
            {
                menu.Commit();
            }
        }
    }

    private void DrainMenuActions()
    {
        while (_menuActions.TryDequeue(out var action))
        {
            action();
        }
    }

    private void Trace(ManageContext context)
    {
        if (!_trace)
        {
            return;
        }

        _log.Debug($"manage: {context.Windows.Count} window(s), {_outputs.Count} output(s), focus {_focusStack.Focused?.Window.Title ?? "none"}");
    }

    private sealed class DragState(
        ManagedWindow window,
        PointerOperation op,
        Edges edges,
        int startX,
        int startY,
        int startWidth,
        int startHeight)
    {
        public ManagedWindow Window { get; } = window;

        public PointerOperation Op { get; } = op;

        public Edges Edges { get; } = edges;

        public int StartX { get; } = startX;

        public int StartY { get; } = startY;

        public int StartWidth { get; } = startWidth;

        public int StartHeight { get; } = startHeight;

        public MagneticBorder Magnet { get; } = new();

        public bool Slide { get; init; }

        public int StartTabOffset { get; init; }

        public bool FromTab { get; init; }

        public StackDrop? StackTarget { get; set; }

        public TileDrop? TileTarget { get; set; }

        public Size? LastProposed { get; set; }

        public Size Desired { get; set; }
    }
}
