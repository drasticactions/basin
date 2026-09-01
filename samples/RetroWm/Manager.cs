using Basin.Config;
using InputCodes = Basin.InputCodes;
using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Protocol = Basin.WindowManager.Skia.Protocol;
using Wayland;
using CursorShape = Basin.WindowManager.Protocol.WpCursorShapeDeviceV1.Shape;

using Basin.Diagnostics;

namespace RetroWm;

internal sealed class Manager
{
    private readonly RiverWindowManager _wm;
    private readonly bool _trace;
    private readonly BasinLogger _log;
    private readonly WlCompositor? _compositor;
    private readonly WlShm? _shm;
    private readonly OutputScales? _scales;
    private readonly PointerInput _pointerInput;

    private readonly WmSession<ManagedWindow> _session;
    private readonly WmFocusStack<ManagedWindow> _focusStack;
    private readonly IReadOnlyList<ManagedWindow> _windows;
    private readonly IReadOnlyDictionary<WmWindow, ManagedWindow> _byWindow;
    private readonly List<WmOutput> _outputs = [];
    private const int WorkspaceCount = 9;

    private readonly bool[] _dockHidden = new bool[WorkspaceCount];

    private readonly Dictionary<WmOutput, OutputGrid[]> _grids = [];
    private int _workspace;
    private readonly Queue<WmAction> _actions = new();
    private readonly List<(BindingMode Mode, KeyBinding Binding)> _bindings = [];
    private readonly HashSet<WmSeat> _armedSeats = [];
    private readonly List<ManagedWindow> _cycle = [];

    private WmSeat? _currentSeat;
    private WmOutput? _currentOutput;
    private BindingMode _mode = BindingMode.Default;
    private EdgeDrag? _edgeDrag;
    private MoveDrag? _moveDrag;
    private Protocol.ZwlrLayerShellV1? _layerShell;
    private MenuSurface? _menu;
    private ManagedWindow? _menuWindow;
    private ManagedWindow? _modeWindow;
    private OutlineSurface? _outline;
    private PreviewSurface? _preview;
    private ManagedWindow? _pointerFrame;
    private bool _pointerOnMenu;
    private DockSurface? _pointerDock;
    private double _pointerX;
    private double _pointerY;
    private (ManagedWindow Window, long At)? _lastIconClick;
    private readonly Dictionary<WmOutput, DockSurface> _docks = [];
    private readonly Dictionary<WmOutput, BackgroundSurface> _backgrounds = [];
    private readonly RetroIconLoader _icons = new();
    private readonly List<DockEntry> _dockEntries = [];
    private readonly List<(ulong Seq, ManagedWindow Window)> _dockOrder = [];
    private (ManagedWindow Window, long At)? _lastBoxClick;
    private (ManagedWindow Window, long At)? _lastTitleClick;
    private ulong _nextMinimizeSeq;

    private const long DoubleClickMs = 400;
    private const double SizeStep = 0.05;

    private readonly bool _noConfig;
    private Config _config;
    private readonly WmReloadSignal _reload;

    internal Manager(RiverWindowManager wm, bool trace, bool noConfig, BasinLogger log)
    {
        _wm = wm;
        _trace = trace;
        _noConfig = noConfig;
        _log = log;
        _config = Config.Load(noConfig, log);
        _focusStack = new WmFocusStack<ManagedWindow>(wm);
        _session = new WmSession<ManagedWindow>(_focusStack, window => new ManagedWindow(window));
        _windows = _session.Windows;
        _byWindow = _session.ByWindow;
        _focusStack.FocusChanged += FollowWindowOutput;
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
        _pointerInput.SurfaceEntered += OnPointerEntered;
        _pointerInput.SurfaceLeft += OnPointerLeft;
        _pointerInput.PointerMoved += OnPointerMoved;
        _pointerInput.ButtonChanged += OnPointerButton;

        wm.Manage += OnManage;
        wm.Render += OnRender;
        if (wm.LayerShell is { } layerShell)
        {
            layerShell.FocusTaken += _ => _actions.Enqueue(WmAction.ClearFocus);
            layerShell.FocusReleased += _ => _actions.Enqueue(WmAction.RestoreFocus);
        }
    }

    private void OnManage(ManageContext context)
    {
        _reload.Process();
        UpdateCurrents(context);
        _session.AdoptNewWindows(context);
        _session.ForgetClosedWindows(context);
        ValidateFullscreen();
        ValidateMenuAndModes();
        EnsureGrids();
        ArmBindings(context);
        RefreshChrome();
        RefreshCapabilities();
        ApplyEdgeDrag();
        ApplyMoveDrag();
        FinishReleasedDrag();
        FinishReleasedMoveDrag();
        _session.DrainInteractions();
        _session.DrainPointerRequests();
        DrainActions();
        DrainWindowEvents();
        ReconcileDocks();
        ReconcileBackgrounds();
        Relayout();
        ApplyBindingModes(context);
        Trace(context);
    }

    private void OnRender(RenderContext context)
    {
        _ = context;
        foreach (var mw in _windows)
        {
            if (mw.Iconized || mw.Workspace != _workspace)
            {
                mw.Window.Hide();
                continue;
            }

            mw.Window.Show();

            if (mw.FullscreenOutput is not null)
            {
                continue;
            }

            var swallow = mw.SwallowTop;
            if (mw.IsDialog)
            {
                PositionDialog(mw);
            }
            else
            {
                mw.Window.Node.SetPosition(mw.X, mw.Y - swallow);
            }

            if (_wm.Version >= 3)
            {
                var reported = mw.Window.Dimensions;
                var oversize = reported.Width > mw.Width || reported.Height > mw.Height + swallow;
                mw.Window.SetContentClipBox(
                    !mw.IsDialog && (swallow > 0 || oversize)
                        ? new Rect(0, swallow, mw.Width, mw.Height)
                        : Rect.Empty);
            }
            else if (_wm.Version >= 2)
            {
                var reported = mw.Window.Dimensions;
                mw.Window.SetClipBox(
                    !mw.IsDialog && (reported.Width > mw.Width || reported.Height > mw.Height)
                        ? new Rect(0, 0, mw.Width, mw.Height)
                        : Rect.Empty);
            }

        }

        RenderFrames();
        Restack();
        RenderBackgrounds();
        RenderDocks();
        RenderMenu();
        RenderDropPreview();
        RenderOutline();
    }

    private void RenderDropPreview()
    {
        if (_moveDrag is { Moved: true, Op.IsEnded: false } drag
            && drag.Preview is { } rect
            && !drag.Output.IsRemoved
            && _compositor is not null && _shm is not null && _layerShell is not null
            && _scales?.ProxyForName(drag.Output.WlOutputName) is { } proxy)
        {
            if (_preview is not null && !ReferenceEquals(_preview.Output, drag.Output))
            {
                _preview.Dispose();
                _preview = null;
            }

            _preview ??= new PreviewSurface(_compositor, _shm, _layerShell, proxy, drag.Output, _wm);
            var local = new Rect(
                rect.X - drag.Output.Position.X,
                rect.Y - drag.Output.Position.Y,
                rect.Width,
                rect.Height);
            _preview.Show(local, _scales.ScaleForName(drag.Output.WlOutputName));
        }
        else if (_preview is not null)
        {
            _preview.Dispose();
            _preview = null;
        }
    }

    private void RenderBackgrounds()
    {
        foreach (var background in _backgrounds.Values)
        {
            if (background.Output.IsRemoved)
            {
                continue;
            }

            var scale = _scales?.ScaleForName(background.Output.WlOutputName) ?? 1;
            if (background.Render(scale))
            {
                background.Commit();
            }
        }
    }

    private void RenderDocks()
    {
        foreach (var dock in _docks.Values)
        {
            var output = dock.Output;
            if (output.IsRemoved)
            {
                continue;
            }

            _dockOrder.Clear();
            foreach (var mw in _windows)
            {
                if (mw.Iconized && mw.Workspace == _workspace
                    && !mw.Window.IsClosed && ReferenceEquals(mw.Output, output))
                {
                    _dockOrder.Add((mw.MinimizeSeq, mw));
                }
            }

            _dockOrder.Sort(static (a, b) => a.Seq.CompareTo(b.Seq));
            var scale = _scales?.ScaleForName(output.WlOutputName) ?? 1;
            _dockEntries.Clear();
            foreach (var (_, mw) in _dockOrder)
            {
                var icon = mw.Window.AppId is { Length: > 0 } appId ? _icons.Load(appId, scale) : null;
                _dockEntries.Add(new DockEntry(mw, TitleFor(mw), icon));
            }

            if (dock.Render(_dockEntries, scale))
            {
                dock.Commit();
            }
        }
    }

    private void RenderMenu()
    {
        if (_menu is { } menu && !menu.Output.IsRemoved)
        {
            var scale = _scales?.ScaleForName(menu.Output.WlOutputName) ?? 1;
            if (menu.Render(scale))
            {
                menu.Commit();
            }
        }
    }

    private void RenderOutline()
    {
        if (_moveDrag is { Moved: true, Op.IsEnded: false } drag
            && !drag.Output.IsRemoved
            && _compositor is not null && _shm is not null && _layerShell is not null
            && _scales?.ProxyForName(drag.Output.WlOutputName) is { } proxy)
        {
            if (_outline is not null && !ReferenceEquals(_outline.Output, drag.Output))
            {
                _outline.Dispose();
                _outline = null;
            }

            _outline ??= new OutlineSurface(_compositor, _shm, _layerShell, proxy, drag.Output, _wm);
            var local = new Rect(
                drag.OutlineRect.X - drag.Output.Position.X,
                drag.OutlineRect.Y - drag.Output.Position.Y,
                drag.OutlineRect.Width,
                drag.OutlineRect.Height);
            _outline.Show(local, _scales.ScaleForName(drag.Output.WlOutputName));
        }
        else if (_outline is not null)
        {
            _outline.Dispose();
            _outline = null;
        }
    }

    private void RenderFrames()
    {
        if (_compositor is null)
        {
            return;
        }

        foreach (var mw in _windows)
        {
            if (mw.Frame is not { } frame)
            {
                continue;
            }

            var wanted = !mw.Iconized && mw.Workspace == _workspace
                && mw.Chrome == WindowChrome.ServerSide
                && mw.FullscreenOutput is null && mw.Width > 0 && mw.Height > 0;
            if (!wanted)
            {
                if (frame.Mapped)
                {
                    frame.SyncNextCommit();
                    frame.Unmap();
                }

                continue;
            }

            var scale = frame.ScaleFor(mw.Output?.WlOutputName ?? 0);
            frame.EnsureGeometry(mw.Width, mw.Height, scale, !mw.IsDialog);
            frame.UpdateInputRegion(_compositor);
            var rendered = frame.Render(TitleFor(mw), ReferenceEquals(mw, _focusStack.Focused));
            frame.SetOffset(mw.SwallowTop);
            if (rendered)
            {
                frame.SyncNextCommit();
                frame.Commit();
            }
        }
    }

    private static string TitleFor(ManagedWindow mw) =>
        mw.Window.Title is { Length: > 0 } title ? title : mw.Window.AppId ?? "Window";

    private void UpdateCurrents(ManageContext context)
    {
        _outputs.Clear();
        foreach (var output in context.Outputs)
        {
            _outputs.Add(output);
            if (!_grids.ContainsKey(output))
            {
                var set = new OutputGrid[WorkspaceCount];
                for (var i = 0; i < WorkspaceCount; i++)
                {
                    set[i] = new OutputGrid();
                }

                _grids[output] = set;
            }
        }

        List<WmOutput>? removed = null;
        foreach (var (output, _) in _grids)
        {
            if (output.IsRemoved)
            {
                (removed ??= []).Add(output);
            }
        }

        if (removed is not null)
        {
            foreach (var output in removed)
            {
                _grids.Remove(output);
            }

            foreach (var mw in _windows)
            {
                if (mw.Output is null or { IsRemoved: true })
                {
                    AssignOutput(mw, FallbackOutput());
                }
            }
        }

        if (_currentSeat is null or { IsRemoved: true })
        {
            _currentSeat = context.Seats.Count > 0 ? context.Seats[0] : null;
        }

        if (_currentOutput is null or { IsRemoved: true })
        {
            _currentOutput = _outputs.Count > 0 ? _outputs[0] : null;
            _layerDefaultSet = false;
        }

        if (!_layerDefaultSet && _wm.LayerShell is not null
            && _currentOutput is { IsRemoved: false } defaultOutput)
        {
            defaultOutput.SetDefaultForLayerSurfaces();
            _layerDefaultSet = true;
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

        ReconcileDocks();
        ReconcileBackgrounds();
    }

    private void ReconcileBackgrounds()
    {
        if (_compositor is null || _shm is null || _layerShell is null || _scales is null)
        {
            return;
        }

        var wanted = Theme.DesktopBg is not null;
        if (wanted)
        {
            foreach (var output in _outputs)
            {
                if (!_backgrounds.ContainsKey(output) && _scales.ProxyForName(output.WlOutputName) is { } proxy)
                {
                    _backgrounds[output] = new BackgroundSurface(
                        _compositor, _shm, _layerShell, proxy, output, _wm);
                }
            }
        }

        List<WmOutput>? stale = null;
        foreach (var (output, background) in _backgrounds)
        {
            if (output.IsRemoved || !wanted)
            {
                background.Dispose();
                (stale ??= []).Add(output);
            }
        }

        if (stale is not null)
        {
            foreach (var output in stale)
            {
                _backgrounds.Remove(output);
            }
        }
    }

    private void ReconcileDocks()
    {
        if (_compositor is null || _shm is null || _layerShell is null || _scales is null)
        {
            return;
        }

        var wanted = !_dockHidden[_workspace];
        if (wanted)
        {
            foreach (var output in _outputs)
            {
                if (!_docks.ContainsKey(output) && _scales.ProxyForName(output.WlOutputName) is { } proxy)
                {
                    _docks[output] = new DockSurface(
                        _compositor, _shm, _layerShell, proxy, output, _wm);
                }
            }
        }

        List<WmOutput>? stale = null;
        foreach (var (output, dock) in _docks)
        {
            if (output.IsRemoved || !wanted)
            {
                if (ReferenceEquals(_pointerDock, dock))
                {
                    _pointerDock = null;
                }

                dock.Dispose();
                (stale ??= []).Add(output);
            }
        }

        if (stale is not null)
        {
            foreach (var output in stale)
            {
                _docks.Remove(output);
            }
        }
    }

    private void OnAdopted(ManagedWindow mw)
    {
        var window = mw.Window;
        mw.Workspace = window.Parent is { } parentWindow
            && _byWindow.TryGetValue(parentWindow, out var managedParent)
            ? managedParent.Workspace
            : _workspace;
        mw.Events.Enqueue((WindowEventKind.Init, null));

        window.MinimizeRequested += () => mw.Events.Enqueue((WindowEventKind.Iconize, null));
        window.MaximizeRequested += () => mw.Events.Enqueue((WindowEventKind.Zoom, null));
        window.UnmaximizeRequested += () => mw.Events.Enqueue((WindowEventKind.Unzoom, null));
        window.FullscreenRequested += output => mw.Events.Enqueue((WindowEventKind.Fullscreen, output));
        window.ExitFullscreenRequested += () => mw.Events.Enqueue((WindowEventKind.Unfullscreen, null));
        window.ShowWindowMenuRequested += _ => mw.Events.Enqueue((WindowEventKind.Menu, null));

        if (_compositor is not null && _shm is not null && _scales is not null)
        {
            mw.Frame = new FrameSurface(window, _compositor, _shm, _scales);
        }

        AssignOutput(mw, PreferredOutputFor(mw));
    }

    private void AssignOutput(ManagedWindow mw, WmOutput? output)
    {
        if (mw.Output is { } previous && TryGridAt(previous, mw.Workspace, out var previousGrid))
        {
            previousGrid.Remove(mw);
        }

        mw.Output = output;
        if (output is not null && !mw.IsDialog && !mw.Iconized
            && TryGridAt(output, mw.Workspace, out var grid))
        {
            grid.Add(mw);
        }
    }

    private bool TryGrid(WmOutput output, out OutputGrid grid) => TryGridAt(output, _workspace, out grid);

    private bool TryGridAt(WmOutput output, int workspace, out OutputGrid grid)
    {
        if (_grids.TryGetValue(output, out var set))
        {
            grid = set[workspace];
            return true;
        }

        grid = null!;
        return false;
    }

    private bool TryGridFor(ManagedWindow mw, out OutputGrid grid)
    {
        if (mw.Output is { IsRemoved: false } output)
        {
            return TryGridAt(output, mw.Workspace, out grid);
        }

        grid = null!;
        return false;
    }

    private WmOutput? PreferredOutputFor(ManagedWindow mw)
    {
        if (mw.Window.Parent is { } parent
            && _byWindow.TryGetValue(parent, out var managedParent)
            && managedParent.Output is { IsRemoved: false } parentOutput)
        {
            return parentOutput;
        }

        if (_currentSeat is { IsRemoved: false } seat)
        {
            var pointer = seat.PointerPosition;
            foreach (var output in _outputs)
            {
                if (output.Area.Contains(pointer))
                {
                    return output;
                }
            }
        }

        return FallbackOutput();
    }

    private WmOutput? FallbackOutput() =>
        _currentOutput is { IsRemoved: false } current ? current : _outputs.Count > 0 ? _outputs[0] : null;

    private void OnForgetting(ManagedWindow mw)
    {
        if (_edgeDrag is { } drag && ReferenceEquals(drag.Window, mw))
        {
            _edgeDrag = null;
            drag.Op.End();
        }

        if (_moveDrag is { } moveDrag && ReferenceEquals(moveDrag.Window, mw))
        {
            _moveDrag = null;
            moveDrag.Op.End();
        }

        if (_lastBoxClick is { } boxClick && ReferenceEquals(boxClick.Window, mw))
        {
            _lastBoxClick = null;
        }

        if (_lastTitleClick is { } titleClick && ReferenceEquals(titleClick.Window, mw))
        {
            _lastTitleClick = null;
        }

        foreach (var set in _grids.Values)
        {
            foreach (var grid in set)
            {
                grid.Remove(mw);
            }
        }

        mw.Frame?.Dispose();
    }

    private void ValidateMenuAndModes()
    {
        if (_menuWindow is { Window.IsClosed: true } || _menu is { Output.IsRemoved: true })
        {
            CloseMenu();
        }

        if (_mode is BindingMode.Move or BindingMode.Size
            && (_modeWindow is null or { Window.IsClosed: true } or { Iconized: true }))
        {
            _modeWindow = null;
            _mode = BindingMode.Default;
        }
    }

    private void ValidateFullscreen()
    {
        foreach (var mw in _windows)
        {
            if (mw.FullscreenOutput is { IsRemoved: true })
            {
                ExitFullscreen(mw);
            }
        }
    }

    private void EnsureGrids()
    {
        foreach (var set in _grids.Values)
        {
            set[_workspace].EnsureFractions();
        }
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

            var main = _config.MainModifier;
            BindDefault(seat, "Escape", main, WmAction.CycleForward);
            BindDefault(seat, "Escape", main | Modifiers.Shift, WmAction.CycleBackward);
            BindDefault(seat, "Tab", main, WmAction.CycleForward);
            BindDefault(seat, "Tab", main | Modifiers.Shift, WmAction.CycleBackward);
            BindDefault(seat, "F4", main, WmAction.Close);
            BindDefault(seat, "Return", main, WmAction.ZoomToggle);
            BindDefault(seat, "Return", main | Modifiers.Shift, WmAction.SpawnTerminal);
            BindDefault(seat, "space", main, WmAction.OpenMenu);
            BindDefault(seat, "i", main, WmAction.Iconize);
            BindDefault(seat, "i", main | Modifiers.Shift, WmAction.RestoreLast);
            BindDefault(seat, "d", main, WmAction.ToggleDock);
            BindDefault(seat, "m", main, WmAction.EnterMoveMode);
            BindDefault(seat, "s", main, WmAction.EnterSizeMode);
            BindDefault(seat, "Left", main, WmAction.FocusLeft);
            BindDefault(seat, "Right", main, WmAction.FocusRight);
            BindDefault(seat, "Up", main, WmAction.FocusUp);
            BindDefault(seat, "Down", main, WmAction.FocusDown);
            BindDefault(seat, "Left", main | Modifiers.Ctrl, WmAction.ArrangeLeft);
            BindDefault(seat, "Right", main | Modifiers.Ctrl, WmAction.ArrangeRight);
            BindDefault(seat, "Up", main | Modifiers.Ctrl, WmAction.ArrangeUp);
            BindDefault(seat, "Down", main | Modifiers.Ctrl, WmAction.ArrangeDown);
            BindDefault(seat, "Left", main | Modifiers.Ctrl | Modifiers.Shift, WmAction.NudgeLeft);
            BindDefault(seat, "Right", main | Modifiers.Ctrl | Modifiers.Shift, WmAction.NudgeRight);
            BindDefault(seat, "Up", main | Modifiers.Ctrl | Modifiers.Shift, WmAction.NudgeUp);
            BindDefault(seat, "Down", main | Modifiers.Ctrl | Modifiers.Shift, WmAction.NudgeDown);
            for (var i = 0; i < WorkspaceCount; i++)
            {
                BindDefault(seat, (i + 1).ToString(), main, WmAction.Workspace1 + i);
                BindDefault(seat, (i + 1).ToString(), main | Modifiers.Shift, WmAction.SendWorkspace1 + i);
            }

            BindDefault(seat, "Left", main | Modifiers.Shift, WmAction.SendLeft);
            BindDefault(seat, "Right", main | Modifiers.Shift, WmAction.SendRight);
            BindDefault(seat, "Up", main | Modifiers.Shift, WmAction.SendUp);
            BindDefault(seat, "Down", main | Modifiers.Shift, WmAction.SendDown);
            BindDefault(seat, "e", main | Modifiers.Shift, WmAction.ExitSession);

            foreach (var hotkey in _config.Hotkeys)
            {
                KeyBinding binding;
                if (hotkey.Action is { } action)
                {
                    binding = _wm.Bindings.Bind(
                        seat, hotkey.Keysym, hotkey.ModifierMask, () => _actions.Enqueue(action));
                }
                else
                {
                    var command = hotkey.Command!;
                    binding = _wm.Bindings.Bind(
                        seat, hotkey.Keysym, hotkey.ModifierMask, () => Spawn(command));
                }

                if (_mode == BindingMode.Default)
                {
                    binding.Enable();
                }

                _bindings.Add((BindingMode.Default, binding));
            }

            Bind(seat, "Up", Modifiers.None, WmAction.MenuUp, BindingMode.Menu);
            Bind(seat, "Down", Modifiers.None, WmAction.MenuDown, BindingMode.Menu);
            Bind(seat, "Return", Modifiers.None, WmAction.MenuActivate, BindingMode.Menu);
            Bind(seat, "Escape", Modifiers.None, WmAction.MenuCancel, BindingMode.Menu);

            Bind(seat, "Left", Modifiers.None, WmAction.MoveLeft, BindingMode.Move);
            Bind(seat, "Right", Modifiers.None, WmAction.MoveRight, BindingMode.Move);
            Bind(seat, "Up", Modifiers.None, WmAction.MoveUp, BindingMode.Move);
            Bind(seat, "Down", Modifiers.None, WmAction.MoveDown, BindingMode.Move);
            Bind(seat, "Return", Modifiers.None, WmAction.ModeExit, BindingMode.Move);
            Bind(seat, "Escape", Modifiers.None, WmAction.ModeExit, BindingMode.Move);

            Bind(seat, "Left", Modifiers.None, WmAction.SizeLeft, BindingMode.Size);
            Bind(seat, "Right", Modifiers.None, WmAction.SizeRight, BindingMode.Size);
            Bind(seat, "Up", Modifiers.None, WmAction.SizeUp, BindingMode.Size);
            Bind(seat, "Down", Modifiers.None, WmAction.SizeDown, BindingMode.Size);
            Bind(seat, "Return", Modifiers.None, WmAction.ModeExit, BindingMode.Size);
            Bind(seat, "Escape", Modifiers.None, WmAction.ModeExit, BindingMode.Size);
        }
    }

    private void BindDefault(WmSeat seat, string keysym, Modifiers modifiers, WmAction action)
    {
        var sym = Keysym.FromName(keysym);
        foreach (var hotkey in _config.Hotkeys)
        {
            if ((hotkey.Keysym == sym && hotkey.ModifierMask == modifiers) || hotkey.Action == action)
            {
                return;
            }
        }

        Bind(seat, keysym, modifiers, action);
    }

    private void Bind(
        WmSeat seat,
        string keysym,
        Modifiers modifiers,
        WmAction action,
        BindingMode mode = BindingMode.Default)
    {
        var binding = _wm.Bindings.Bind(seat, keysym, modifiers, () => _actions.Enqueue(action));
        if (mode == _mode)
        {
            binding.Enable();
        }

        _bindings.Add((mode, binding));
    }

    private void ApplyBindingModes(ManageContext context)
    {
        BindingMode? live = context.SessionIsLocked ? null : _mode;
        foreach (var (mode, binding) in _bindings)
        {
            var want = live == mode;
            if (want && !binding.IsEnabled)
            {
                binding.Enable();
            }
            else if (!want && binding.IsEnabled)
            {
                binding.Disable();
            }
        }
    }

    private void RefreshChrome()
    {
        foreach (var mw in _windows)
        {
            var forceSsd = false;
            int? swallowTop = null;
            foreach (var rule in _config.Rules)
            {
                if (rule.Matches(mw.Window.AppId, mw.Window.Title, mw.Window.DecorationHint, mw.IsDialog))
                {
                    forceSsd |= rule.ForceSsd;
                    swallowTop = rule.SwallowTop ?? swallowTop;
                }
            }

            mw.RuleForceSsd = forceSsd;
            mw.RuleSwallowTop = swallowTop ?? 0;
            ApplyChrome(mw);
        }
    }

    private void ApplyChrome(ManagedWindow mw)
    {
        var acceptsAFrame = mw.Window.DecorationHint
            is DecorationHint.PrefersServerSide or DecorationHint.NoPreference;
        var wantsFrame = _config.Decorations switch
        {
            DecorationPreference.ForceSsd => true,
            DecorationPreference.PreferSsd => mw.RuleForceSsd || acceptsAFrame,
            _ => mw.RuleForceSsd,
        };
        var desired = mw.Frame is not null && wantsFrame
            ? WindowChrome.ServerSide
            : WindowChrome.ClientSide;
        mw.Chrome = desired;
        if (desired == mw.SentChrome)
        {
            return;
        }

        mw.SentChrome = desired;
        if (desired == WindowChrome.ServerSide)
        {
            mw.Window.UseServerSideDecorations();
        }
        else
        {
            mw.Window.UseClientSideDecorations();
        }
    }

    private void RefreshCapabilities()
    {
        foreach (var mw in _windows)
        {
            var capabilities = WindowCapabilities.None;
            if (!mw.IsDialog)
            {
                capabilities |= WindowCapabilities.WindowMenu | WindowCapabilities.Minimize;
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

    private void ApplyEdgeDrag()
    {
        if (_edgeDrag is not { Op.IsEnded: false } drag || drag.Window.Window.IsClosed)
        {
            return;
        }

        if (drag.Output.IsRemoved || !TryGrid(drag.Output, out var grid))
        {
            _edgeDrag = null;
            drag.Op.End();
            return;
        }

        if (grid.ColumnFractions.Count != drag.StartColumns.Length
            || drag.Column >= grid.RowFractions.Count
            || grid.RowFractions[drag.Column].Count != drag.StartRows.Length)
        {
            _edgeDrag = null;
            drag.Op.End();
            return;
        }

        var area = WmOutputPolicy.UsableArea(drag.Output);
        if (area.IsEmpty)
        {
            return;
        }

        var delta = drag.Op.Delta;
        if (drag.ColumnBoundary > 0)
        {
            OutputGrid.ShiftBoundary(
                grid.ColumnFractions, drag.StartColumns, drag.ColumnBoundary, (double)delta.X / area.Width);
        }

        if (drag.RowBoundary > 0)
        {
            OutputGrid.ShiftBoundary(
                grid.RowFractions[drag.Column], drag.StartRows, drag.RowBoundary, (double)delta.Y / area.Height);
        }
    }

    private void FinishReleasedDrag()
    {
        if (_edgeDrag is not { } drag || drag.Op.IsEnded || !drag.Op.IsReleased)
        {
            return;
        }

        _edgeDrag = null;
        if (!drag.Window.Window.IsClosed)
        {
            drag.Window.Window.InformResizing(false);
        }

        drag.Op.End();
    }

    private void OnInteraction(ManagedWindow mw)
    {
        Focus(mw);
        HandleInteraction(mw);
    }

    private void HandleInteraction(ManagedWindow mw)
    {
        var hadMenuFor = _menuWindow;
        if (_menu is not null)
        {
            CloseMenu();
        }

        if (_mode is BindingMode.Move or BindingMode.Size)
        {
            _modeWindow = null;
            _mode = BindingMode.Default;
        }

        if (_currentSeat is not { IsRemoved: false } seat
            || _moveDrag is { Op.IsEnded: false }
            || _edgeDrag is { Op.IsEnded: false })
        {
            return;
        }

        if (mw.Chrome != WindowChrome.ServerSide || mw.Frame is not { } frame)
        {
            return;
        }

        var frameRect = FrameRect(mw);
        var pointer = seat.PointerPosition;
        var local = new Point(pointer.X - frameRect.X, pointer.Y - frameRect.Y);
        if (!new Rect(0, 0, frameRect.Width, frameRect.Height).Contains(local))
        {
            return;
        }

        var part = frame.PartAt(local.X, local.Y);
        var now = Environment.TickCount64;
        switch (part)
        {
            case FramePart.SystemBox:
                if (_lastBoxClick is { } boxClick && ReferenceEquals(boxClick.Window, mw)
                    && now - boxClick.At <= DoubleClickMs)
                {
                    _lastBoxClick = null;
                    mw.Window.Close();
                    return;
                }

                _lastBoxClick = (mw, now);
                if (!ReferenceEquals(hadMenuFor, mw))
                {
                    OpenSystemMenu(mw);
                }

                break;
            case FramePart.Title:
                if (_lastTitleClick is { } titleClick && ReferenceEquals(titleClick.Window, mw)
                    && now - titleClick.At <= DoubleClickMs)
                {
                    _lastTitleClick = null;
                    ToggleZoomOf(mw);
                    return;
                }

                _lastTitleClick = (mw, now);
                StartMoveDrag(mw, seat);
                break;
            case FramePart.Border:
                var edges = EdgeAt(frame, local);
                if (edges != Edges.None)
                {
                    StartEdgeDrag(mw, seat, edges);
                }

                break;
        }
    }

    private static Edges EdgeAt(FrameSurface frame, Point local)
    {
        var bw = Theme.BorderWidth;
        var edges = Edges.None;
        if (local.X < bw)
        {
            edges |= Edges.Left;
        }
        else if (local.X >= frame.FrameWidth - bw)
        {
            edges |= Edges.Right;
        }

        if (local.Y < bw)
        {
            edges |= Edges.Top;
        }
        else if (local.Y >= frame.FrameHeight - bw)
        {
            edges |= Edges.Bottom;
        }

        return edges;
    }

    private static Rect FrameRect(ManagedWindow mw)
    {
        if (mw.Chrome != WindowChrome.ServerSide)
        {
            return mw.ContentRect;
        }

        var (horizontal, top, bottom) = Theme.InsetsFor(!mw.IsDialog);
        return new Rect(
            mw.X - horizontal,
            mw.Y - top,
            mw.Width + (horizontal * 2),
            mw.Height + top + bottom);
    }

    private void OnReload()
    {
        _config = Config.Load(_noConfig, _log);
        _log.Info($"configuration reloaded");

        foreach (var (_, binding) in _bindings)
        {
            binding.Destroy();
        }

        _bindings.Clear();
        _armedSeats.Clear();

        foreach (var mw in _windows)
        {
            mw.Frame?.Invalidate();
        }

        _icons.Clear();
        foreach (var dock in _docks.Values)
        {
            dock.Invalidate();
        }

        foreach (var background in _backgrounds.Values)
        {
            background.Invalidate();
        }

        if (_menu is not null)
        {
            CloseMenu();
        }
    }

    private void OnPointerRequest(ManagedWindow mw, WmSeat seat, Edges? edges)
    {
        if (_edgeDrag is { Op.IsEnded: false })
        {
            return;
        }

        Focus(mw);
        if (edges is { } resizeEdges)
        {
            StartEdgeDrag(mw, seat, resizeEdges);
        }
    }

    private void StartEdgeDrag(ManagedWindow mw, WmSeat seat, Edges edges)
    {
        if (mw.Zoomed || mw.Iconized || mw.IsDialog || mw.FullscreenOutput is not null)
        {
            return;
        }

        if (mw.Output is not { IsRemoved: false } output || !TryGridFor(mw, out var grid))
        {
            return;
        }

        var index = grid.CellIndexOf(mw);
        if (index < 0 || index >= grid.Cells.Count)
        {
            return;
        }

        var (col, row) = grid.Cells[index];
        if (edges == Edges.None)
        {
            var pointer = seat.PointerPosition;
            edges = (pointer.X < mw.X + (mw.Width / 2) ? Edges.Left : Edges.Right)
                | (pointer.Y < mw.Y + (mw.Height / 2) ? Edges.Top : Edges.Bottom);
        }

        var columnBoundary = (edges & Edges.Left) != 0 ? col : (edges & Edges.Right) != 0 ? col + 1 : -1;
        if (columnBoundary <= 0 || columnBoundary >= grid.ColumnCount)
        {
            columnBoundary = -1;
        }

        var rows = grid.RowFractions[col];
        var rowBoundary = (edges & Edges.Top) != 0 ? row : (edges & Edges.Bottom) != 0 ? row + 1 : -1;
        if (rowBoundary <= 0 || rowBoundary >= rows.Count)
        {
            rowBoundary = -1;
        }

        if (columnBoundary < 0 && rowBoundary < 0)
        {
            return;
        }

        var op = seat.StartPointerOperation();
        _edgeDrag = new EdgeDrag(
            mw, op, output, col, columnBoundary, rowBoundary,
            [.. grid.ColumnFractions], [.. rows]);
        mw.Window.InformResizing(true);
    }

    private void DrainActions()
    {
        while (_actions.TryDequeue(out var action))
        {
            Execute(action);
        }
    }

    private void OnPointerEntered(uint seatName, uint surfaceId, double x, double y)
    {
        _pointerFrame = FrameWindowFor(surfaceId);
        _pointerOnMenu = _menu?.SurfaceId == surfaceId;
        _pointerDock = DockFor(surfaceId);
        _pointerX = x;
        _pointerY = y;
        if (_pointerDock is not null)
        {
            _pointerInput.SetShape(seatName, CursorShape.Default);
            return;
        }

        if (_pointerOnMenu)
        {
            _pointerInput.SetShape(seatName, CursorShape.Default);
            if (_menu!.UpdateHover((int)x, (int)y))
            {
                RenderMenu();
            }

            return;
        }

        UpdateFrameCursor(seatName, x, y);
    }

    private void OnPointerLeft(uint seatName, uint surfaceId)
    {
        _ = seatName;
        if (_pointerFrame?.Frame?.SurfaceId == surfaceId)
        {
            _pointerFrame = null;
        }

        if (_menu is { } menu && menu.SurfaceId == surfaceId)
        {
            _pointerOnMenu = false;
            if (menu.ClearHover())
            {
                RenderMenu();
            }
        }

        if (_pointerDock?.SurfaceId == surfaceId)
        {
            _pointerDock = null;
        }
    }

    private void OnPointerMoved(uint seatName, double x, double y)
    {
        _pointerX = x;
        _pointerY = y;
        if (_pointerDock is not null)
        {
            return;
        }

        if (_pointerOnMenu && _menu is { } menu)
        {
            if (menu.UpdateHover((int)x, (int)y))
            {
                RenderMenu();
            }

            return;
        }

        if (_pointerFrame is not null)
        {
            UpdateFrameCursor(seatName, x, y);
        }
    }

    private void OnPointerButton(uint seatName, uint button, bool isPressed)
    {
        _ = seatName;
        if (button != InputCodes.BtnLeft)
        {
            return;
        }

        if (_pointerOnMenu && !isPressed && _menu is { Hovered: >= 0 })
        {
            _actions.Enqueue(WmAction.MenuActivate);
            _wm.RequestManage();
            return;
        }

        if (_pointerDock is { } dock && isPressed)
        {
            var restoring = false;
            if (dock.IconAt((int)_pointerX, (int)_pointerY) is { } icon)
            {
                var now = Environment.TickCount64;
                var doubleClick = _lastIconClick is { } last && ReferenceEquals(last.Window, icon)
                    && now - last.At <= DoubleClickMs;
                _lastIconClick = (icon, now);
                dock.Selected = icon;
                if (doubleClick)
                {
                    _pendingRestore = icon;
                    _actions.Enqueue(WmAction.RestoreIcon);
                    restoring = true;
                }
            }
            else
            {
                dock.Selected = null;
            }

            if (restoring)
            {
                _wm.RequestManage();
            }
            else
            {
                RenderDocks();
            }
        }
    }

    private ManagedWindow? _pendingRestore;
    private bool _layerDefaultSet;

    private DockSurface? DockFor(uint surfaceId)
    {
        foreach (var dock in _docks.Values)
        {
            if (dock.SurfaceId == surfaceId)
            {
                return dock;
            }
        }

        return null;
    }

    private ManagedWindow? FrameWindowFor(uint surfaceId)
    {
        foreach (var mw in _windows)
        {
            if (mw.Frame?.SurfaceId == surfaceId)
            {
                return mw;
            }
        }

        return null;
    }

    private void UpdateFrameCursor(uint seatName, double x, double y)
    {
        if (_pointerFrame is not { Frame: { } frame })
        {
            return;
        }

        var local = new Point((int)x, (int)y);
        var shape = frame.PartAt(local.X, local.Y) == FramePart.Border
            ? ShapeFor(EdgeAt(frame, local))
            : CursorShape.Default;
        _pointerInput.SetShape(seatName, shape);
    }

    private static CursorShape ShapeFor(Edges edges) => edges switch
    {
        Edges.Top | Edges.Left => CursorShape.NwResize,
        Edges.Top | Edges.Right => CursorShape.NeResize,
        Edges.Bottom | Edges.Left => CursorShape.SwResize,
        Edges.Bottom | Edges.Right => CursorShape.SeResize,
        Edges.Left => CursorShape.WResize,
        Edges.Right => CursorShape.EResize,
        Edges.Top => CursorShape.NResize,
        Edges.Bottom => CursorShape.SResize,
        _ => CursorShape.Default,
    };

    private void Execute(WmAction action)
    {
        switch (action)
        {
            case WmAction.CycleForward:
                CycleFocus(1);
                break;
            case WmAction.CycleBackward:
                CycleFocus(-1);
                break;
            case WmAction.Close:
                _focusStack.Focused?.Window.Close();
                break;
            case WmAction.SpawnTerminal:
                Spawn(_config.TerminalCommand);
                break;
            case WmAction.ExitSession:
                if (_wm.Version >= 4)
                {
                    _wm.ExitSession();
                }

                break;
            case WmAction.ClearFocus:
                ClearFocus();
                break;
            case WmAction.RestoreFocus:
                RestoreFocusFromStack();
                break;
            case WmAction.ZoomToggle:
                ZoomToggle();
                break;
            case WmAction.SendLeft:
                SendToOutput(-1, 0);
                break;
            case WmAction.SendRight:
                SendToOutput(1, 0);
                break;
            case WmAction.SendUp:
                SendToOutput(0, -1);
                break;
            case WmAction.SendDown:
                SendToOutput(0, 1);
                break;
            case WmAction.OpenMenu:
                if (_focusStack.Focused is { } forMenu)
                {
                    OpenSystemMenu(forMenu);
                }

                break;
            case WmAction.Iconize:
                if (_focusStack.Focused is { } toIconize)
                {
                    Iconize(toIconize);
                }

                break;
            case WmAction.MenuUp:
                _menu?.MoveSelection(-1);
                break;
            case WmAction.MenuDown:
                _menu?.MoveSelection(1);
                break;
            case WmAction.MenuActivate:
                ActivateMenuItem();
                break;
            case WmAction.MenuCancel:
                CloseMenu();
                break;
            case WmAction.RestoreIcon:
                if (_pendingRestore is { } toRestore)
                {
                    _pendingRestore = null;
                    RestoreIcon(toRestore);
                }

                break;
            case WmAction.MoveLeft:
                MoveArrow(_modeWindow, -1, 0);
                break;
            case WmAction.MoveRight:
                MoveArrow(_modeWindow, 1, 0);
                break;
            case WmAction.MoveUp:
                MoveArrow(_modeWindow, 0, -1);
                break;
            case WmAction.MoveDown:
                MoveArrow(_modeWindow, 0, 1);
                break;
            case WmAction.SizeLeft:
                SizeNudge(_modeWindow, -1, 0);
                break;
            case WmAction.SizeRight:
                SizeNudge(_modeWindow, 1, 0);
                break;
            case WmAction.SizeUp:
                SizeNudge(_modeWindow, 0, -1);
                break;
            case WmAction.SizeDown:
                SizeNudge(_modeWindow, 0, 1);
                break;
            case WmAction.ModeExit:
                _modeWindow = null;
                _mode = BindingMode.Default;
                break;
            case WmAction.FocusLeft:
                FocusDirection(-1, 0);
                break;
            case WmAction.FocusRight:
                FocusDirection(1, 0);
                break;
            case WmAction.FocusUp:
                FocusDirection(0, -1);
                break;
            case WmAction.FocusDown:
                FocusDirection(0, 1);
                break;
            case WmAction.ArrangeLeft:
                MoveArrow(_focusStack.Focused, -1, 0);
                break;
            case WmAction.ArrangeRight:
                MoveArrow(_focusStack.Focused, 1, 0);
                break;
            case WmAction.ArrangeUp:
                MoveArrow(_focusStack.Focused, 0, -1);
                break;
            case WmAction.ArrangeDown:
                MoveArrow(_focusStack.Focused, 0, 1);
                break;
            case WmAction.NudgeLeft:
                SizeNudge(_focusStack.Focused, -1, 0);
                break;
            case WmAction.NudgeRight:
                SizeNudge(_focusStack.Focused, 1, 0);
                break;
            case WmAction.NudgeUp:
                SizeNudge(_focusStack.Focused, 0, -1);
                break;
            case WmAction.NudgeDown:
                SizeNudge(_focusStack.Focused, 0, 1);
                break;
            case WmAction.EnterMoveMode:
                EnterArrangeMode(BindingMode.Move);
                break;
            case WmAction.EnterSizeMode:
                EnterArrangeMode(BindingMode.Size);
                break;
            case WmAction.RestoreLast:
                RestoreLastIcon();
                break;
            case WmAction.ToggleDock:
                _dockHidden[_workspace] = !_dockHidden[_workspace];
                break;
            default:
                if (action >= WmAction.Workspace1 && action <= WmAction.Workspace9)
                {
                    SwitchWorkspace(action - WmAction.Workspace1);
                }
                else if (action >= WmAction.SendWorkspace1 && action <= WmAction.SendWorkspace9)
                {
                    SendToWorkspace(action - WmAction.SendWorkspace1);
                }

                break;
        }
    }

    private void SwitchWorkspace(int index)
    {
        if (index < 0 || index >= WorkspaceCount || index == _workspace)
        {
            return;
        }

        AbortDrags();
        CloseMenu();
        if (_mode is BindingMode.Move or BindingMode.Size)
        {
            _modeWindow = null;
            _mode = BindingMode.Default;
        }

        _workspace = index;
        foreach (var dock in _docks.Values)
        {
            dock.Selected = null;
        }

        FocusNextOn(_currentOutput);
    }

    private void SendToWorkspace(int index)
    {
        if (index < 0 || index >= WorkspaceCount || index == _workspace
            || _focusStack.Focused is not { } mw || mw.IsDialog || mw.Window.IsClosed)
        {
            return;
        }

        if (mw.FullscreenOutput is not null)
        {
            ExitFullscreen(mw);
        }

        if (TryGridFor(mw, out var grid))
        {
            grid.Remove(mw);
        }

        mw.Workspace = index;
        if (mw.Output is { IsRemoved: false } output && TryGridAt(output, index, out var target))
        {
            target.Add(mw);
        }

        for (var pass = 0; pass < WorkspaceCount; pass++)
        {
            var moved = false;
            foreach (var child in _windows)
            {
                if (child.IsDialog && child.Workspace != index
                    && child.Window.Parent is { } parent
                    && _byWindow.TryGetValue(parent, out var managedParent)
                    && managedParent.Workspace == index)
                {
                    child.Workspace = index;
                    moved = true;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        FocusNextOn(mw.Output);
    }

    private void AbortDrags()
    {
        if (_moveDrag is { } moveDrag)
        {
            _moveDrag = null;
            if (!moveDrag.Op.IsEnded)
            {
                moveDrag.Op.End();
            }
        }

        if (_edgeDrag is { } edgeDrag)
        {
            _edgeDrag = null;
            if (!edgeDrag.Op.IsEnded)
            {
                if (!edgeDrag.Window.Window.IsClosed)
                {
                    edgeDrag.Window.Window.InformResizing(false);
                }

                edgeDrag.Op.End();
            }
        }
    }

    private void EnterArrangeMode(BindingMode mode)
    {
        if (_focusStack.Focused is not { } mw || !CanArrange(mw))
        {
            return;
        }

        CloseMenu();
        _modeWindow = mw;
        _mode = mode;
    }

    private bool CanArrange(ManagedWindow mw) =>
        !mw.IsDialog && !mw.Iconized && !mw.Zoomed && mw.FullscreenOutput is null
        && TryGridFor(mw, out var grid)
        && grid.Count > 1 && grid.PositionOf(mw).Column >= 0;

    private void FocusDirection(int dx, int dy)
    {
        if (_focusStack.Focused is not { } mw || mw.Output is not { IsRemoved: false } output
            || !TryGridFor(mw, out var grid))
        {
            return;
        }

        var index = grid.CellIndexOf(mw);
        if (index < 0 || index >= grid.Cells.Count)
        {
            return;
        }

        var (col, row) = grid.Cells[index];
        if (dy != 0)
        {
            var targetRow = row + dy;
            for (var j = 0; j < grid.Cells.Count; j++)
            {
                if (grid.Cells[j] == (col, targetRow))
                {
                    Focus(grid.Tiles[j]);
                    return;
                }
            }

            return;
        }

        var targetColumn = col + dx;
        if (targetColumn < 0 || targetColumn >= grid.ColumnCount)
        {
            return;
        }

        var area = WmOutputPolicy.UsableArea(output);
        if (area.IsEmpty)
        {
            return;
        }

        var myFrame = grid.FrameFor(index, area);
        var centerY = myFrame.Y + (myFrame.Height / 2);
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var j = 0; j < grid.Cells.Count; j++)
        {
            if (grid.Cells[j].Column != targetColumn)
            {
                continue;
            }

            var frame = grid.FrameFor(j, area);
            if (centerY >= frame.Y && centerY < frame.Bottom)
            {
                best = j;
                break;
            }

            var distance = Math.Min(Math.Abs(centerY - frame.Y), Math.Abs(centerY - frame.Bottom));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = j;
            }
        }

        if (best >= 0)
        {
            Focus(grid.Tiles[best]);
        }
    }

    private void RestoreLastIcon()
    {
        ManagedWindow? best = null;
        var anyOutput = true;
        for (var pass = 0; pass < 2 && best is null; pass++)
        {
            anyOutput = pass == 1;
            foreach (var mw in _windows)
            {
                if (!mw.Iconized || mw.Workspace != _workspace || mw.Window.IsClosed)
                {
                    continue;
                }

                if (!anyOutput && _currentOutput is { IsRemoved: false }
                    && !ReferenceEquals(mw.Output, _currentOutput))
                {
                    continue;
                }

                if (best is null || mw.MinimizeSeq > best.MinimizeSeq)
                {
                    best = mw;
                }
            }
        }

        if (best is not null)
        {
            RestoreIcon(best);
        }
    }

    private void OpenSystemMenu(ManagedWindow mw)
    {
        CloseMenu();
        if (mw.IsDialog || mw.Iconized || mw.Window.IsClosed
            || _compositor is null || _shm is null || _layerShell is null || _scales is null)
        {
            return;
        }

        if (mw.Output is not { IsRemoved: false } output
            || _scales.ProxyForName(output.WlOutputName) is not { } proxy
            || !TryGridFor(mw, out var grid))
        {
            return;
        }

        var scale = _scales.ScaleForName(output.WlOutputName);
        var menu = new MenuSurface(_compositor, _shm, _layerShell, proxy, output, _wm, scale);
        var (menuColumn, _) = grid.PositionOf(mw);
        var hasNeighbors = grid.Count > 1;
        menu.SetEnabled(SystemMenuItem.Move, hasNeighbors && !mw.Zoomed && menuColumn >= 0);
        menu.SetEnabled(SystemMenuItem.Size, hasNeighbors && !mw.Zoomed && menuColumn >= 0);
        menu.SetEnabled(SystemMenuItem.Icon, true);
        menu.SetEnabled(SystemMenuItem.Zoom, !mw.IsFixedSize);
        menu.SetEnabled(SystemMenuItem.Close, true);

        var frameRect = FrameRect(mw);
        var area = output.Dimensions;
        var x = Math.Clamp(
            frameRect.X - output.Position.X,
            0,
            Math.Max(area.Width - menu.SurfaceSize.Width, 0));
        var y = Math.Clamp(
            mw.Y - output.Position.Y,
            0,
            Math.Max(area.Height - menu.SurfaceSize.Height, 0));
        menu.Origin = new Point(x, y);
        menu.ApplyPosition();
        menu.SelectFirstEnabled();
        _menu = menu;
        _menuWindow = mw;
        _mode = BindingMode.Menu;
    }

    private void ActivateMenuItem()
    {
        if (_menu is not { Hovered: >= 0 } menu || _menuWindow is not { } mw)
        {
            CloseMenu();
            return;
        }

        var item = (SystemMenuItem)menu.Hovered;
        CloseMenu();
        if (mw.Window.IsClosed)
        {
            return;
        }

        switch (item)
        {
            case SystemMenuItem.Move:
                _modeWindow = mw;
                _mode = BindingMode.Move;
                break;
            case SystemMenuItem.Size:
                _modeWindow = mw;
                _mode = BindingMode.Size;
                break;
            case SystemMenuItem.Icon:
                Iconize(mw);
                break;
            case SystemMenuItem.Zoom:
                ToggleZoomOf(mw);
                break;
            case SystemMenuItem.Close:
                mw.Window.Close();
                break;
        }
    }

    private void CloseMenu()
    {
        _menu?.Dispose();
        _menu = null;
        _menuWindow = null;
        _pointerOnMenu = false;
        if (_mode == BindingMode.Menu)
        {
            _mode = BindingMode.Default;
        }
    }

    private void MoveArrow(ManagedWindow? window, int dx, int dy)
    {
        if (window is not { } mw || !TryGridFor(mw, out var grid))
        {
            return;
        }

        if (mw.IsDialog || mw.Iconized || mw.Zoomed || mw.FullscreenOutput is not null)
        {
            return;
        }

        var (col, row) = grid.PositionOf(mw);
        if (col < 0)
        {
            return;
        }

        if (dy != 0)
        {
            grid.ReorderInColumn(mw, dy);
            return;
        }

        var targetColumn = col + dx;
        if (targetColumn < 0 || targetColumn >= grid.ColumnCount)
        {
            grid.SplitAtEdge(mw, after: dx > 0);
            return;
        }

        grid.MoveIntoColumn(mw, targetColumn, row);
    }

    private void SizeNudge(ManagedWindow? window, int dx, int dy)
    {
        if (window is not { } mw || !TryGridFor(mw, out var grid))
        {
            return;
        }

        if (mw.IsDialog || mw.Iconized || mw.Zoomed || mw.FullscreenOutput is not null)
        {
            return;
        }

        var (col, row) = grid.PositionOf(mw);
        if (col < 0)
        {
            return;
        }

        if (dx != 0)
        {
            var boundary = col + 1 < grid.ColumnCount ? col + 1 : col;
            OutputGrid.NudgeBoundary(grid.ColumnFractions, boundary, dx * SizeStep);
        }

        if (dy != 0)
        {
            var rows = grid.RowFractions[col];
            var boundary = row + 1 < rows.Count ? row + 1 : row;
            OutputGrid.NudgeBoundary(rows, boundary, dy * SizeStep);
        }
    }

    private void RestoreIcon(ManagedWindow mw)
    {
        if (mw.Window.IsClosed || !mw.Iconized)
        {
            return;
        }

        mw.Iconized = false;
        mw.Workspace = _workspace;
        var output = mw.Output is { IsRemoved: false } own ? own : FallbackOutput();
        mw.Output = null;
        AssignOutput(mw, output);
        Focus(mw);
    }

    private void Iconize(ManagedWindow mw)
    {
        if (mw.IsDialog || mw.Iconized || mw.Window.IsClosed)
        {
            return;
        }

        var wasFocused = ReferenceEquals(_focusStack.Focused, mw);
        if (mw.Zoomed)
        {
            SetZoom(mw, false);
        }

        mw.Iconized = true;
        mw.MinimizeSeq = _nextMinimizeSeq++;
        if (TryGridFor(mw, out var grid))
        {
            grid.Remove(mw);
        }

        if (wasFocused)
        {
            FocusNextOn(mw.Output);
        }
    }

    private void FocusNextOn(WmOutput? output)
    {
        foreach (var candidate in _focusStack)
        {
            if (!candidate.Iconized && candidate.Workspace == _workspace && !candidate.Window.IsClosed
                && (output is null || candidate.Output is null || ReferenceEquals(candidate.Output, output)))
            {
                Focus(candidate);
                return;
            }
        }

        foreach (var candidate in _windows)
        {
            if (!candidate.Iconized && candidate.Workspace == _workspace && !candidate.Window.IsClosed)
            {
                Focus(candidate);
                return;
            }
        }

        ClearFocus();
    }

    private void StartMoveDrag(ManagedWindow mw, WmSeat seat)
    {
        if (mw.Iconized || mw.IsDialog || mw.Zoomed || mw.FullscreenOutput is not null)
        {
            return;
        }

        if (mw.Output is not { IsRemoved: false } output)
        {
            return;
        }

        var op = seat.StartPointerOperation();
        _moveDrag = new MoveDrag(mw, op, output, FrameRect(mw));
    }

    private void ApplyMoveDrag()
    {
        if (_moveDrag is not { Op.IsEnded: false } drag || drag.Window.Window.IsClosed)
        {
            return;
        }

        var delta = drag.Op.Delta;
        if (!drag.Moved && Math.Abs(delta.X) + Math.Abs(delta.Y) > 3)
        {
            drag.Moved = true;
        }

        if (!drag.Moved)
        {
            return;
        }

        drag.OutlineRect = new Rect(
            drag.StartFrame.X + delta.X,
            drag.StartFrame.Y + delta.Y,
            drag.StartFrame.Width,
            drag.StartFrame.Height);
        UpdatePreview(drag);
    }

    private void UpdatePreview(MoveDrag drag)
    {
        var output = drag.Output;
        if (output.IsRemoved || !TryGrid(output, out var grid))
        {
            ClearPreview(drag);
            return;
        }

        var pointer = drag.Op.Seat.PointerPosition;
        if (_docks.ContainsKey(output) && DockStripRect(output).Contains(pointer))
        {
            if (!drag.PreviewOnDock)
            {
                drag.PreviewOnDock = true;
                drag.PreviewTarget = null;
                drag.Preview = DockStripRect(output);
            }

            return;
        }

        var area = WmOutputPolicy.UsableArea(output);
        if (area.IsEmpty)
        {
            ClearPreview(drag);
            return;
        }

        if (FindDropTarget(grid, area, pointer, drag.Window) is not { } hit)
        {
            ClearPreview(drag);
            return;
        }

        if (drag.PreviewOnDock || !ReferenceEquals(drag.PreviewTarget, hit.Target)
            || drag.PreviewKind != hit.Kind || drag.PreviewCount != grid.Count)
        {
            drag.PreviewOnDock = false;
            drag.PreviewTarget = hit.Target;
            drag.PreviewKind = hit.Kind;
            drag.PreviewCount = grid.Count;
            drag.Preview = grid.PreviewDrop(drag.Window, hit.Target, hit.Kind, area);
        }
    }

    private static void ClearPreview(MoveDrag drag)
    {
        drag.Preview = null;
        drag.PreviewTarget = null;
        drag.PreviewOnDock = false;
    }

    private Rect DockStripRect(WmOutput output) => new(
        output.Position.X,
        output.Position.Y + output.Dimensions.Height - Theme.DockHeight,
        output.Dimensions.Width,
        Theme.DockHeight);

    private void FinishReleasedMoveDrag()
    {
        if (_moveDrag is not { } drag || drag.Op.IsEnded || !drag.Op.IsReleased)
        {
            return;
        }

        _moveDrag = null;
        drag.Op.End();
        if (drag.Window.Window.IsClosed || !drag.Moved)
        {
            return;
        }

        Drop(drag.Window, drag.Op.Seat.PointerPosition, drag.Output);
    }

    private void Drop(ManagedWindow mw, Point pointer, WmOutput output)
    {
        if (output.IsRemoved || !TryGrid(output, out var grid))
        {
            return;
        }

        if (_docks.ContainsKey(output) && DockStripRect(output).Contains(pointer))
        {
            Iconize(mw);
            return;
        }

        if (!grid.Contains(mw))
        {
            return;
        }

        var area = WmOutputPolicy.UsableArea(output);
        if (area.IsEmpty)
        {
            return;
        }

        if (FindDropTarget(grid, area, pointer, mw) is not { } hit)
        {
            return;
        }

        switch (hit.Kind)
        {
            case DropKind.StackAbove:
                grid.StackOn(mw, hit.Target, below: false);
                break;
            case DropKind.StackBelow:
                grid.StackOn(mw, hit.Target, below: true);
                break;
            case DropKind.SplitLeft:
                grid.SplitBeside(mw, hit.Target, after: false);
                break;
            case DropKind.SplitRight:
                grid.SplitBeside(mw, hit.Target, after: true);
                break;
            default:
                grid.Swap(mw, hit.Target);
                break;
        }
    }

    private static (ManagedWindow Target, DropKind Kind)? FindDropTarget(
        OutputGrid grid,
        Rect area,
        Point pointer,
        ManagedWindow mw)
    {
        for (var j = 0; j < grid.Tiles.Count; j++)
        {
            var target = grid.Tiles[j];
            if (ReferenceEquals(target, mw))
            {
                continue;
            }

            var frame = grid.FrameFor(j, area);
            if (frame.Contains(pointer))
            {
                return (target, DropKindIn(frame, pointer));
            }
        }

        return null;
    }

    private static DropKind DropKindIn(Rect frame, Point pointer)
    {
        var x = pointer.X - frame.X;
        var y = pointer.Y - frame.Y;
        if (y < frame.Height / 4)
        {
            return DropKind.StackAbove;
        }

        if (y >= frame.Height - (frame.Height / 4))
        {
            return DropKind.StackBelow;
        }

        if (x < frame.Width / 4)
        {
            return DropKind.SplitLeft;
        }

        if (x >= frame.Width - (frame.Width / 4))
        {
            return DropKind.SplitRight;
        }

        return DropKind.Swap;
    }

    private void ZoomToggle()
    {
        if (_focusStack.Focused is { } mw)
        {
            ToggleZoomOf(mw);
        }
    }

    private static void ToggleZoomOf(ManagedWindow mw)
    {
        if (mw.FullscreenOutput is not null)
        {
            ExitFullscreen(mw);
        }
        else if (mw.Zoomed)
        {
            SetZoom(mw, false);
        }
        else
        {
            SetZoom(mw, true);
        }
    }

    private static void SetZoom(ManagedWindow mw, bool zoomed)
    {
        if (mw.IsDialog || mw.Iconized || mw.Zoomed == zoomed || (zoomed && mw.IsFixedSize))
        {
            return;
        }

        mw.Zoomed = zoomed;
        mw.Window.InformMaximized(zoomed);
    }

    private void SendToOutput(int dx, int dy)
    {
        if (_focusStack.Focused is not { } mw || _outputs.Count <= 1)
        {
            return;
        }

        var current = mw.Output is { IsRemoved: false } own ? own : _currentOutput;
        if (current is null)
        {
            return;
        }

        WmOutput? best = null;
        var bestDistance = long.MaxValue;
        foreach (var candidate in _outputs)
        {
            if (ReferenceEquals(candidate, current))
            {
                continue;
            }

            var ddx = candidate.Position.X - current.Position.X;
            var ddy = candidate.Position.Y - current.Position.Y;
            if (dx != 0 && Math.Sign(ddx) != dx)
            {
                continue;
            }

            if (dy != 0 && Math.Sign(ddy) != dy)
            {
                continue;
            }

            var distance = ((long)ddx * ddx) + ((long)ddy * ddy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best is null)
        {
            return;
        }

        AssignOutput(mw, best);
        if (mw.FullscreenOutput is not null)
        {
            mw.Window.Fullscreen(best);
            mw.FullscreenOutput = best;
        }

        SetCurrentOutput(best);
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
                Focus(mw);
                break;
            case WindowEventKind.Close:
                mw.Window.Close();
                break;
            case WindowEventKind.Zoom:
                SetZoom(mw, true);
                break;
            case WindowEventKind.Unzoom:
                SetZoom(mw, false);
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
            case WindowEventKind.Iconize:
                Iconize(mw);
                break;
            case WindowEventKind.Menu:
                OpenSystemMenu(mw);
                break;
        }
    }

    private static void EnterFullscreen(ManagedWindow mw, WmOutput output)
    {
        mw.Window.Fullscreen(output);
        mw.Window.InformFullscreen(true);
        mw.FullscreenOutput = output;
    }

    private static void ExitFullscreen(ManagedWindow mw)
    {
        mw.Window.ExitFullscreen();
        mw.Window.InformFullscreen(false);
        mw.FullscreenOutput = null;
    }

    private void CollectCycleOrder(List<ManagedWindow> into)
    {
        into.Clear();
        foreach (var output in _outputs)
        {
            if (TryGrid(output, out var grid))
            {
                foreach (var mw in grid.Tiles)
                {
                    if (!mw.Iconized && !mw.Window.IsClosed)
                    {
                        into.Add(mw);
                    }
                }
            }
        }

        foreach (var mw in _windows)
        {
            if (mw.IsDialog && !mw.Iconized && mw.Workspace == _workspace)
            {
                into.Add(mw);
            }
        }
    }

    private void CycleFocus(int direction)
    {
        CollectCycleOrder(_cycle);
        if (_cycle.Count == 0)
        {
            return;
        }

        var index = _focusStack.Focused is { } focused ? _cycle.IndexOf(focused) : -1;
        index = (((index + direction) % _cycle.Count) + _cycle.Count) % _cycle.Count;
        Focus(_cycle[index]);
    }

    private void Focus(ManagedWindow mw) => _focusStack.Focus(mw);

    private void FollowWindowOutput(ManagedWindow mw)
    {
        if (mw.Output is { IsRemoved: false } output)
        {
            SetCurrentOutput(output);
        }
    }

    private void SetCurrentOutput(WmOutput output)
    {
        if (_wm.LayerShell is not null
            && (!_layerDefaultSet || !ReferenceEquals(_currentOutput, output)))
        {
            output.SetDefaultForLayerSurfaces();
            _layerDefaultSet = true;
        }

        _currentOutput = output;
    }

    private void ClearFocus() => _focusStack.ClearFocus();

    private void RestoreFocusFromStack() =>
        _focusStack.RestoreFromStack(mw => !mw.Iconized && mw.Workspace == _workspace);

    private void Relayout()
    {
        foreach (var output in _outputs)
        {
            if (!TryGrid(output, out var grid))
            {
                continue;
            }

            var area = WmOutputPolicy.UsableArea(output);
            if (area.IsEmpty)
            {
                continue;
            }

            grid.EnsureFractions();
            for (var i = 0; i < grid.Tiles.Count; i++)
            {
                var mw = grid.Tiles[i];
                if (mw.FullscreenOutput is not null)
                {
                    continue;
                }

                PlaceTile(mw, mw.Zoomed ? area : grid.FrameFor(i, area));
            }
        }

        foreach (var mw in _windows)
        {
            if (mw.IsDialog && !mw.Iconized && mw.Window.Dimensions.IsEmpty)
            {
                mw.Window.ProposeDimensions(0, 0);
            }
        }
    }

    private static void PlaceTile(ManagedWindow mw, Rect frame)
    {
        var (horizontal, top, bottom) = mw.Chrome == WindowChrome.ServerSide
            ? Theme.InsetsFor(titled: !mw.IsDialog)
            : (0, 0, 0);
        var content = new Rect(
            frame.X + horizontal,
            frame.Y + top,
            Math.Max(frame.Width - (horizontal * 2), 1),
            Math.Max(frame.Height - top - bottom, 1));
        mw.SetFrame(content);
        mw.Window.SetTiled(Edges.All);
        mw.Propose(content.Width, content.Height + mw.SwallowTop);
    }

    private void PositionDialog(ManagedWindow mw)
    {
        var size = mw.Window.Dimensions;
        if (size.IsEmpty)
        {
            return;
        }

        if (mw.Window.Parent is { } parent && _byWindow.TryGetValue(parent, out var managedParent))
        {
            var cx = managedParent.X + ((managedParent.Width - size.Width) / 2);
            var cy = managedParent.Y + ((managedParent.Height - size.Height) / 2);
            mw.SetFrame(new Rect(cx, cy, size.Width, size.Height));
        }
        else if (mw.Output is { } output)
        {
            var area = WmOutputPolicy.UsableArea(output);
            mw.SetFrame(new Rect(
                area.X + ((area.Width - size.Width) / 2),
                area.Y + ((area.Height - size.Height) / 2),
                size.Width,
                size.Height));
        }

        mw.Window.Node.SetPosition(mw.X, mw.Y);
    }

    private void Restack()
    {
        foreach (var mw in _windows)
        {
            if (mw.Zoomed && !mw.Iconized && !mw.Window.IsClosed)
            {
                mw.Window.Node.PlaceTop();
            }
        }

        foreach (var mw in _windows)
        {
            if (mw.IsDialog && !mw.Iconized && mw.Window.Parent is { } parent
                && _byWindow.TryGetValue(parent, out var managedParent)
                && !managedParent.Window.IsClosed)
            {
                mw.Window.Node.PlaceAbove(managedParent.Window.Node);
            }
        }

        foreach (var mw in _windows)
        {
            if (mw.FullscreenOutput is not null && !mw.Window.IsClosed)
            {
                mw.Window.Node.PlaceTop();
            }
        }
    }

    private void Spawn(string[] argv)
    {
        if (WmSpawn.Run(argv) is { } failure)
        {
            _log.Error($"could not spawn '{(argv[0])}': {failure}");
        }
    }

    private void Trace(ManageContext context)
    {
        if (!_trace)
        {
            return;
        }

        _log.Debug($"manage: {context.Windows.Count} window(s), {context.Outputs.Count} output(s), {context.NewWindows.Count} new, {context.ClosedWindows.Count} closed, workspace {(_workspace + 1)}");
        foreach (var mw in _windows)
        {
            _log.Debug($"  {(ReferenceEquals(mw, _focusStack.Focused) ? '*' : ' ')} '{(mw.Window.AppId ?? "?")}' at {mw.X},{mw.Y} {mw.Width}x{mw.Height}{((mw.Workspace != _workspace ? $" ws={mw.Workspace + 1}" : string.Empty) +
                (mw.Iconized ? " iconized" : string.Empty) +
                (mw.Zoomed ? " zoomed" : string.Empty) +
                (mw.FullscreenOutput is not null ? " fullscreen" : string.Empty) +
                (mw.IsDialog ? " dialog" : string.Empty))}");
        }
    }

    private sealed class MoveDrag(ManagedWindow window, PointerOperation op, WmOutput output, Rect startFrame)
    {
        public ManagedWindow Window { get; } = window;

        public PointerOperation Op { get; } = op;

        public WmOutput Output { get; } = output;

        public Rect StartFrame { get; } = startFrame;

        public bool Moved { get; set; }

        public Rect OutlineRect { get; set; }

        public Rect? Preview { get; set; }

        public ManagedWindow? PreviewTarget { get; set; }

        public DropKind PreviewKind { get; set; }

        public int PreviewCount { get; set; }

        public bool PreviewOnDock { get; set; }
    }

    private sealed class EdgeDrag(
        ManagedWindow window,
        PointerOperation op,
        WmOutput output,
        int column,
        int columnBoundary,
        int rowBoundary,
        double[] startColumns,
        double[] startRows)
    {
        public ManagedWindow Window { get; } = window;

        public PointerOperation Op { get; } = op;

        public WmOutput Output { get; } = output;

        public int Column { get; } = column;

        public int ColumnBoundary { get; } = columnBoundary;

        public int RowBoundary { get; } = rowBoundary;

        public double[] StartColumns { get; } = startColumns;

        public double[] StartRows { get; } = startRows;
    }
}
