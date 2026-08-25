using InputCodes = Basin.InputCodes;
using Basin.WindowManager;
using CursorShape = Basin.WindowManager.Protocol.WpCursorShapeDeviceV1.Shape;
using Wayland;

using Basin.Diagnostics;

namespace Dinghy;

internal sealed class Manager
{
    private const int CascadePadding = 10;
    private const long CloseDoubleClickMs = 400;

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
    private readonly List<(BindingMode Mode, KeyBinding Binding)> _bindings = [];
    private readonly HashSet<WmSeat> _armedSeats = [];
    private readonly List<ManagedWindow> _cascade = [];
    private readonly List<(ManagedWindow Child, ManagedWindow Parent, int Depth)> _restack = [];

    private readonly PointerInput _pointerInput;

    private WmSeat? _currentSeat;
    private WmOutput? _currentOutput;
    private WmOutput? _lastFocusedOutput;
    private ulong _nextMinimizeSeq;

    private DragState? _drag;
    private ManagedWindow? _pointerTitlebar;
    private double _titlebarX;
    private double _titlebarY;
    private (ManagedWindow Window, long At)? _lastCloseClick;

    private readonly Dictionary<WmOutput, DesktopSurface> _desktops = [];
    private readonly Dictionary<WmOutput, WallpaperSurface> _wallpapers = [];
    private readonly IconLoader _icons = new();
    private BindingMode _mode = BindingMode.Default;
    private MenuSurface? _menu;
    private MenuMode? _menuMode;
    private ShieldSurface? _shield;
    private List<ManagedWindow>? _altTabStack;
    private List<ManagedWindow>? _pendingStackRestore;
    private ManagedWindow? _altTabFocused;
    private ManagedWindow? _altTabPreview;
    private bool _altTabPreviewWasHidden;
    private WmOutput? _iconFocusOutput;
    private (ManagedWindow Window, long At)? _lastIconClick;
    private (WmOutput Output, Point Position)? _pendingPointerMenu;
    private DesktopSurface? _pointerDesktop;
    private bool _pointerOnMenu;
    private double _managerSurfaceX;
    private double _managerSurfaceY;

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
        _focusStack = new WmFocusStack<ManagedWindow>(wm);
        _session = new WmSession<ManagedWindow>(_focusStack, window => new ManagedWindow(window));
        _windows = _session.Windows;
        _byWindow = _session.ByWindow;
        _focusStack.Focusing += DemoteOtherFullscreen;
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
        _pointerInput.SurfaceEntered += OnTitlebarEntered;
        _pointerInput.SurfaceLeft += OnTitlebarLeft;
        _pointerInput.PointerMoved += OnTitlebarMotion;
        _pointerInput.ButtonChanged += OnTitlebarButton;

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
        SyncAllDimensions();
        RefreshChrome();
        ArmBindings(context);
        RefreshCapabilities();
        ApplyDrag(allowResize: true);
        FinishReleasedDrag();
        RefreshHeldHover();
        _session.DrainInteractions();
        _session.DrainPointerRequests();
        DrainActions();
        DrainWindowEvents();
        OpenPendingPointerMenu();
        ValidateIconFocus();
        ApplyBindingModes(context);
        ReassociateOutputs();
        UpdateCursor();
        Trace(context);
    }

    private void OnRender(RenderContext context)
    {
        RefreshOutputs(context.Outputs);
        SyncAllDimensions();

        ApplyDrag(allowResize: false);
        ApplyInitialPositions();

        foreach (var mw in _windows)
        {
            if (mw.Hidden)
            {
                mw.Window.Hide();
                continue;
            }

            mw.Window.Show();

            mw.Window.ClearBorders();

            if (_wm.Version >= 3)
            {
                mw.Window.SetContentClipBox(mw.SwallowTop > 0
                    ? new Rect(0, mw.SwallowTop, mw.Width, Math.Max(mw.Height - mw.SwallowTop, 1))
                    : Rect.Empty);
            }
        }

        RenderDecorations();

        if (_menuMode == MenuMode.AltTab && _altTabStack is { } altTabOrder)
        {
            RestoreStackOrder(altTabOrder);
        }
        else if (_pendingStackRestore is { } restore)
        {
            _pendingStackRestore = null;
            RestoreStackOrder(restore);
        }

        Restack();
        RenderDesktops();
        RenderMenuAndShield();
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

        if (_lastFocusedOutput is { IsRemoved: true })
        {
            _lastFocusedOutput = null;
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
                if (_scales.ProxyForName(output.WlOutputName) is not { } proxy)
                {
                    continue;
                }

                if (!_desktops.ContainsKey(output))
                {
                    _desktops[output] = new DesktopSurface(_compositor, _shm, _layerShell, proxy, output, _wm);
                }

                if (_config.DesktopWallpaper && !_wallpapers.ContainsKey(output))
                {
                    _wallpapers[output] = new WallpaperSurface(_compositor, _shm, _layerShell, proxy, output, _wm);
                }
                else if (!_config.DesktopWallpaper && _wallpapers.Remove(output, out var stale))
                {
                    stale.Dispose();
                }
            }

            List<WmOutput>? removed = null;
            foreach (var (output, desktop) in _desktops)
            {
                if (output.IsRemoved)
                {
                    desktop.Dispose();
                    (removed ??= []).Add(output);
                }
            }

            if (removed is not null)
            {
                foreach (var output in removed)
                {
                    _desktops.Remove(output);
                    if (_wallpapers.Remove(output, out var wallpaper))
                    {
                        wallpaper.Dispose();
                    }

                    _cascadeSteps.Remove(output);
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
            mw.Shadow = new ShadowSurface(window, _compositor, _shm, _scales);
            mw.Titlebar = new TitlebarSurface(window, _compositor, _shm, _scales);
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

        if (ReferenceEquals(_pointerTitlebar, mw))
        {
            _pointerTitlebar = null;
        }

        _altTabStack?.Remove(mw);
        if (ReferenceEquals(_altTabPreview, mw))
        {
            _altTabPreview = null;
            _altTabPreviewWasHidden = false;
        }

        if (ReferenceEquals(_altTabFocused, mw))
        {
            _altTabFocused = null;
        }

        if (_lastCloseClick is { } close && ReferenceEquals(close.Window, mw))
        {
            _lastCloseClick = null;
        }

        if (_lastIconClick is { } click && ReferenceEquals(click.Window, mw))
        {
            _lastIconClick = null;
        }

        mw.Titlebar?.Dispose();
        mw.Shadow?.Dispose();
    }

    private void SyncAllDimensions()
    {
        foreach (var mw in _windows)
        {
            mw.SyncDimensions();
        }
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
            Bind(seat, "w", main, WmAction.Close);
            Bind(seat, "Down", main, WmAction.SmartHideFocused);
            Bind(seat, "Up", main, WmAction.MaximizeFocused);
            Bind(seat, "Left", main, WmAction.SnapLeft);
            Bind(seat, "Right", main, WmAction.SnapRight);
            Bind(seat, "Left", Modifiers.Super | Modifiers.Alt, WmAction.SendToPreviousOutput);
            Bind(seat, "Up", Modifiers.Super | Modifiers.Alt, WmAction.SendToPreviousOutput);
            Bind(seat, "Right", Modifiers.Super | Modifiers.Alt, WmAction.SendToNextOutput);
            Bind(seat, "Down", Modifiers.Super | Modifiers.Alt, WmAction.SendToNextOutput);
            Bind(seat, "Return", main, WmAction.ToggleFullscreen);
            Bind(seat, "Return", main | Modifiers.Shift, WmAction.SpawnTerminal);
            Bind(seat, "space", main, WmAction.SpawnLauncher);
            Bind(seat, "l", main, WmAction.SpawnLock);
            Bind(seat, "h", main, WmAction.HideFocused);
            Bind(seat, "m", main, WmAction.HideFocused);

            Bind(seat, "Tab", main, WmAction.WindowMenuCycle);
            Bind(seat, "Tab", main | Modifiers.Shift, WmAction.WindowMenuCycle);
            Bind(seat, "grave", main, WmAction.WindowMenuCycleApp);
            Bind(seat, "Escape", main, WmAction.WindowMenuCancel);
            Bind(seat, "Escape", main | Modifiers.Shift, WmAction.WindowMenuCancel);
            BindRelease(seat, main == Modifiers.Alt ? "Alt_L" : "Super_L", WmAction.WindowMenuCommit);
            BindRelease(seat, main == Modifiers.Alt ? "Alt_R" : "Super_R", WmAction.WindowMenuCommit);

            Bind(seat, "Right", Modifiers.None, WmAction.IconSelectNext, BindingMode.DesktopIcons);
            Bind(seat, "Tab", Modifiers.None, WmAction.IconSelectNext, BindingMode.DesktopIcons);
            Bind(seat, "Left", Modifiers.None, WmAction.IconSelectPrevious, BindingMode.DesktopIcons);
            Bind(seat, "Tab", Modifiers.Shift, WmAction.IconSelectPrevious, BindingMode.DesktopIcons);
            Bind(seat, "Up", Modifiers.None, WmAction.IconSelectUp, BindingMode.DesktopIcons);
            Bind(seat, "Down", Modifiers.None, WmAction.IconSelectDown, BindingMode.DesktopIcons);
            Bind(seat, "Return", Modifiers.None, WmAction.IconActivate, BindingMode.DesktopIcons);
            Bind(seat, "Escape", Modifiers.None, WmAction.IconCancel, BindingMode.DesktopIcons);

            foreach (var hotkey in _config.Hotkeys)
            {
                var command = hotkey.Command;
                var binding = _wm.Bindings.Bind(seat, hotkey.Keysym, hotkey.ModifierMask, () => Spawn(command));
                if (_mode == BindingMode.Default)
                {
                    binding.Enable();
                }

                _bindings.Add((BindingMode.Default, binding));
            }

            BindPointer(seat, InputCodes.BtnLeft, main, WmAction.PointerMove);
            BindPointer(seat, InputCodes.BtnRight, main, WmAction.PointerResize);
        }
    }

    private void BindPointer(WmSeat seat, uint button, Modifiers modifiers, WmAction action)
    {
        var binding = seat.BindPointer(button, modifiers, () => _actions.Enqueue(action));
        binding.Enable();
    }

    private void Bind(
        WmSeat seat,
        string keysym,
        Modifiers modifiers,
        WmAction action,
        BindingMode mode = BindingMode.Default)
    {
        if (mode == BindingMode.Default
            && _config.Hotkeys.Any(hotkey =>
                hotkey.Keysym == Keysym.FromName(keysym) && hotkey.ModifierMask == modifiers))
        {
            return;
        }

        var binding = _wm.Bindings.Bind(seat, keysym, modifiers, () => _actions.Enqueue(action));
        if (mode == _mode)
        {
            binding.Enable();
        }

        _bindings.Add((mode, binding));
    }

    private void BindRelease(WmSeat seat, string keysym, WmAction action)
    {
        var binding = _wm.Bindings.Bind(seat, keysym, Modifiers.None);
        binding.Released += () => _actions.Enqueue(action);
        if (_mode == BindingMode.Default)
        {
            binding.Enable();
        }

        _bindings.Add((BindingMode.Default, binding));
    }

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
        var desired = mw.Titlebar is not null && (mw.RuleForceSsd || acceptsAFrame)
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

    private static (int Border, int Titlebar) Insets(ManagedWindow mw) =>
        mw.Chrome == WindowChrome.ServerSide
            ? (Theme.BorderWidthFor(mw.FrameStyle), Theme.TitlebarHeight)
            : (0, 0);

    private void RenderDecorations()
    {
        if (_compositor is null)
        {
            return;
        }

        foreach (var mw in _windows)
        {
            if (mw.Shadow is not { } shadow || mw.Hidden || mw.Width <= 0 || mw.Height <= 0)
            {
                continue;
            }

            var focused = ReferenceEquals(mw, _focusStack.Focused);
            var shadowsOn = Theme.ShadowsEnabled && mw.FullscreenOutput is null && !mw.Maximized;
            var shadowSize = !shadowsOn ? 0
                : focused ? Theme.ShadowsActiveSize : Theme.ShadowsInactiveSize;

            var bufferShadowSize = shadowsOn
                ? Math.Max(Theme.ShadowsActiveSize, Theme.ShadowsInactiveSize)
                : 0;
            var (bw, th) = Insets(mw);
            var contentHeight = Math.Max(mw.Height - mw.SwallowTop, 1);
            var frameWidth = Math.Max(mw.Width + (bw * 2), 1);
            var frameHeight = Math.Max(contentHeight + th + (bw * 2), 1);

            var shadowFrameHeight = Math.Max(frameHeight - (shadowSize / 2), 1);
            var scale = shadow.ScaleFor(mw.Output?.WlOutputName ?? 0);

            shadow.EnsureBuffer(frameWidth, frameHeight, bufferShadowSize, scale);
            shadow.UpdateInputRegion(_compositor);
            var rendered = shadow.Render(frameWidth, shadowFrameHeight, shadowSize, Theme.ShadowsColor, scale);
            shadow.SetOffset(
                -bw - bufferShadowSize,
                -bw - th + mw.SwallowTop - bufferShadowSize + (shadowSize / 2));
            if (rendered)
            {
                shadow.SyncNextCommit();
                shadow.Commit();
            }
        }

        foreach (var mw in _windows)
        {
            if (mw.Titlebar is not { } titlebar)
            {
                continue;
            }

            if (mw.Hidden || mw.Chrome == WindowChrome.ClientSide)
            {
                if (titlebar.Mapped)
                {
                    titlebar.SyncNextCommit();
                    titlebar.Unmap();
                }

                continue;
            }

            if (mw.Width <= 0 || mw.Height <= 0)
            {
                continue;
            }

            var style = mw.FrameStyle;
            var scale = titlebar.ScaleFor(mw.Output?.WlOutputName ?? 0);
            titlebar.EnsureBuffer(mw.Width, Math.Max(mw.Height - mw.SwallowTop, 1), scale, style);
            titlebar.UpdateInputRegion(_compositor);
            var rendered = titlebar.Render(
                mw.Window.Title,
                ReferenceEquals(mw, _focusStack.Focused),
                mw.Maximized,
                showMinimize: !mw.IsDialog,
                showMaximize: !mw.IsDialog && !mw.IsFixedSize,
                style,
                mw.TitlebarHovered,
                mw.TitlebarLeftDown);
            titlebar.SetOffset(mw.SwallowTop);
            if (rendered)
            {
                titlebar.SyncNextCommit();
                titlebar.Commit();
            }
        }
    }

    private void OnInteraction(ManagedWindow mw)
    {
        if (_menu is not null)
        {
            CloseWindowMenu();
        }

        if (_iconFocusOutput is not null)
        {
            ExitIconFocus();
        }

        Focus(mw);
        HandleInteraction(mw);
    }

    private void HandleInteraction(ManagedWindow mw)
    {
        if (_currentSeat is not { IsRemoved: false } seat || _drag is { Op.IsEnded: false })
        {
            return;
        }

        var pointer = seat.PointerPosition;
        var edges = mw.IsFixedSize ? Edges.None : EdgesNearBorder(FrameRect(mw), Theme.BorderWidth, pointer);
        if (edges != Edges.None)
        {
            ClearMaximizedWithoutRestore(mw);
            StartDrag(mw, seat, edges);
            return;
        }

        if (mw.Chrome != WindowChrome.ServerSide)
        {
            return;
        }

        var localX = pointer.X - mw.X;
        var localY = pointer.Y - (mw.Y - Theme.TitlebarHeight + mw.SwallowTop);
        if (localX < 0 || localX >= mw.Width || localY < 0 || localY >= Theme.TitlebarHeight)
        {
            return;
        }

        var (close, hide, max) = TitlebarSurface.ButtonRects(mw.Width, !mw.IsDialog, !mw.IsDialog && !mw.IsFixedSize);
        var point = new Point(localX, localY);
        if (close.Contains(point) || hide?.Contains(point) == true || max?.Contains(point) == true)
        {
            return;
        }

        UnmaximizeForMove(mw, pointer, adjustY: false);
        StartDrag(mw, seat, Edges.None);
    }

    private void OnPointerRequest(ManagedWindow mw, WmSeat seat, Edges? edges)
    {
        if (_drag is { Op.IsEnded: false })
        {
            return;
        }

        if (_menu is not null)
        {
            CloseWindowMenu();
        }

        if (_iconFocusOutput is not null)
        {
            ExitIconFocus();
        }

        Focus(mw);
        if (edges is not { } resizeEdges)
        {
            UnmaximizeForMove(mw, seat.PointerPosition, adjustY: true);
            mw.Snap = null;
            StartDrag(mw, seat, Edges.None);
            return;
        }

        if (mw.IsFixedSize)
        {
            return;
        }

        ClearMaximizedWithoutRestore(mw);
        mw.Snap = null;
        if (resizeEdges == Edges.None)
        {
            var pointer = seat.PointerPosition;
            resizeEdges = (pointer.X < mw.X + (mw.Width / 2) ? Edges.Left : Edges.Right)
                | (pointer.Y < mw.Y + (mw.Height / 2) ? Edges.Top : Edges.Bottom);
        }

        StartDrag(mw, seat, resizeEdges);
    }

    private void StartDrag(ManagedWindow mw, WmSeat seat, Edges edges)
    {
        var op = seat.StartPointerOperation();
        _drag = new DragState(mw, op, edges, mw.X, mw.Y, mw.Width, mw.Height);
        if (edges != Edges.None)
        {
            mw.Window.InformResizing(true);
        }
    }

    private void ApplyDrag(bool allowResize)
    {
        if (_drag is not { Op.IsEnded: false } drag || drag.Window.Window.IsClosed)
        {
            return;
        }

        var delta = drag.Op.Delta;
        var mw = drag.Window;
        if (drag.Edges == Edges.None)
        {
            mw.SetPosition(drag.StartX + delta.X, drag.StartY + delta.Y);
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
        var newX = drag.StartX;
        var newY = drag.StartY;
        if ((drag.Edges & Edges.Left) != 0)
        {
            newWidth = Math.Max(drag.StartWidth - delta.X, minWidth);
            newX = drag.StartX + (drag.StartWidth - newWidth);
        }
        else if ((drag.Edges & Edges.Right) != 0)
        {
            newWidth = Math.Max(drag.StartWidth + delta.X, minWidth);
        }

        if ((drag.Edges & Edges.Top) != 0)
        {
            newHeight = Math.Max(drag.StartHeight - delta.Y, minHeight);
            newY = drag.StartY + (drag.StartHeight - newHeight);
        }
        else if ((drag.Edges & Edges.Bottom) != 0)
        {
            newHeight = Math.Max(drag.StartHeight + delta.Y, minHeight);
        }

        if (newX != mw.X || newY != mw.Y)
        {
            mw.SetPosition(newX, newY);
        }

        mw.Window.ProposeDimensions(newWidth, newHeight);
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
        }

        drag.Op.End();
        UpdateOutputFromRect(drag.Window);
    }

    private void RefreshHeldHover()
    {
        if (_currentSeat is not { } seat)
        {
            return;
        }

        foreach (var mw in _windows)
        {
            if (!mw.TitlebarLeftDown)
            {
                continue;
            }

            var bw = Theme.BorderWidthFor(mw.FrameStyle);
            var localX = seat.PointerPosition.X - (mw.X - bw);
            var localY = seat.PointerPosition.Y - (mw.Y - bw - Theme.TitlebarHeight + mw.SwallowTop);
            SetHover(mw, mw.Titlebar?.ButtonAt(
                mw.Width, bw, localX, localY, !mw.IsDialog, !mw.IsDialog && !mw.IsFixedSize));
        }
    }

    private void SetHover(ManagedWindow mw, TitlebarButton? hovered)
    {
        if (mw.TitlebarHovered != hovered)
        {
            mw.TitlebarHovered = hovered;
            _wm.RequestManage();
        }
    }

    private static void ClearMaximizedWithoutRestore(ManagedWindow mw)
    {
        if (mw.Maximized)
        {
            mw.Maximized = false;
            mw.Window.InformMaximized(false);
        }
    }

    private static void UnmaximizeForMove(ManagedWindow mw, Point pointer, bool adjustY)
    {
        var wasMaximized = mw.Maximized;
        if (!wasMaximized && mw.Snap is null)
        {
            return;
        }

        var saved = mw.PreSnap;
        mw.PreSnap = null;
        mw.Maximized = false;
        mw.Snap = null;

        if (saved is { } restore)
        {
            var currentWidth = Math.Max(mw.Width, 1);
            var currentHeight = Math.Max(mw.Height, 1);
            var relX = (pointer.X - mw.X) / (float)currentWidth;
            var relY = (pointer.Y - (mw.Y - mw.SwallowTop)) / (float)currentHeight;
            mw.Propose(restore.Width, restore.Height);
            var newX = pointer.X - (int)MathF.Round(relX * restore.Width);
            var newY = adjustY ? pointer.Y - (int)MathF.Round(relY * restore.Height) : mw.Y;
            mw.SetPosition(newX, newY);
        }

        if (wasMaximized)
        {
            mw.Window.InformMaximized(false);
        }
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
        UnmaximizeForMove(mw, seat.PointerPosition, adjustY: true);
        mw.Snap = null;
        StartDrag(mw, seat, Edges.None);
    }

    private void StartPointerResize()
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
        if (mw.IsFixedSize)
        {
            return;
        }

        ClearMaximizedWithoutRestore(mw);
        mw.Snap = null;

        var pointer = seat.PointerPosition;
        var edges = (pointer.X < mw.X + (mw.Width / 2) ? Edges.Left : Edges.Right)
            | (pointer.Y < mw.Y + (mw.Height / 2) ? Edges.Top : Edges.Bottom);
        StartDrag(mw, seat, edges);
    }

    private static Rect FrameRect(ManagedWindow mw)
    {
        var bw = Theme.BorderWidth;
        return new Rect(
            mw.X - bw,
            mw.Y - bw - Theme.TitlebarHeight + mw.SwallowTop,
            mw.Width + (bw * 2),
            mw.Height + (bw * 2) + Theme.TitlebarHeight - mw.SwallowTop);
    }

    private static Edges EdgesNearBorder(Rect frame, int border, Point p)
    {
        if (frame.IsEmpty || border <= 0)
        {
            return Edges.None;
        }

        var withinVertical = p.Y >= frame.Y - border && p.Y <= frame.Bottom + border;
        var withinHorizontal = p.X >= frame.X - border && p.X <= frame.Right + border;
        var distLeft = Math.Abs(p.X - frame.X);
        var distRight = Math.Abs(p.X - frame.Right);
        var distTop = Math.Abs(p.Y - frame.Y);
        var distBottom = Math.Abs(p.Y - frame.Bottom);

        var edges = Edges.None;
        if (withinVertical)
        {
            if (distLeft <= border)
            {
                edges |= Edges.Left;
            }

            if (distRight <= border && (edges == Edges.None || distRight < distLeft))
            {
                edges = (edges & ~Edges.Left) | Edges.Right;
            }
        }

        if (withinHorizontal)
        {
            var vertical = Edges.None;
            if (distTop <= border)
            {
                vertical = Edges.Top;
            }

            if (distBottom <= border && (vertical == Edges.None || distBottom < distTop))
            {
                vertical = Edges.Bottom;
            }

            edges |= vertical;
        }

        return edges;
    }

    private void OnTitlebarEntered(uint seatName, uint surfaceId, double x, double y)
    {
        _pointerTitlebar = null;
        _pointerDesktop = null;
        _pointerOnMenu = false;
        _managerSurfaceX = x;
        _managerSurfaceY = y;

        if (TitlebarWindowFor(surfaceId) is { } titlebar)
        {
            _pointerTitlebar = titlebar;
            _titlebarX = x;
            _titlebarY = y;
            SetHover(titlebar, HoverAt(titlebar, x, y));
            UpdateHoverCursor(titlebar, seatName, x, y);
            return;
        }

        if (DesktopFor(surfaceId) is { } desktop)
        {
            _pointerDesktop = desktop;
            UpdateDesktopCursor(desktop, seatName, x, y);
            return;
        }

        if (_menu is { } menu && menu.SurfaceId == surfaceId)
        {
            _pointerOnMenu = true;
            _pointerInput.SetShape(seatName, CursorShape.Default);
            if (menu.UpdateHover((int)x, (int)y))
            {
                _wm.RequestManage();
            }

            return;
        }

        if (_shield is { } shield && shield.SurfaceId == surfaceId)
        {
            _pointerInput.HideCursor(seatName);
        }
    }

    private void OnTitlebarLeft(uint seatName, uint surfaceId)
    {
        _ = seatName;
        _pointerDesktop = null;
        _pointerOnMenu = false;
        if (_pointerTitlebar is { } mw && mw.Titlebar?.SurfaceId == surfaceId)
        {
            var changed = mw.TitlebarHovered is not null || mw.TitlebarPressed is not null || mw.TitlebarLeftDown;
            mw.TitlebarHovered = null;
            mw.TitlebarPressed = null;
            mw.TitlebarLeftDown = false;
            _pointerTitlebar = null;
            if (changed)
            {
                _wm.RequestManage();
            }
        }
    }

    private void OnTitlebarMotion(uint seatName, double x, double y)
    {
        _managerSurfaceX = x;
        _managerSurfaceY = y;
        if (_pointerTitlebar is { } mw)
        {
            _titlebarX = x;
            _titlebarY = y;
            SetHover(mw, HoverAt(mw, x, y));
            UpdateHoverCursor(mw, seatName, x, y);
            return;
        }

        if (_pointerDesktop is { } desktop)
        {
            UpdateDesktopCursor(desktop, seatName, x, y);
            return;
        }

        if (_pointerOnMenu && _menu is { } menu && menu.UpdateHover((int)x, (int)y))
        {
            _wm.RequestManage();
        }
    }

    private void OnTitlebarButton(uint seatName, uint button, bool isPressed)
    {
        _ = seatName;
        if (_pointerDesktop is { } desktop)
        {
            OnDesktopButton(desktop, button, isPressed);
            return;
        }

        if (_pointerOnMenu)
        {
            if (!isPressed && _menuMode == MenuMode.Pointer)
            {
                _actions.Enqueue(WmAction.ActivateMenuHovered);
                _wm.RequestManage();
            }

            return;
        }

        if (button != InputCodes.BtnLeft || _pointerTitlebar is not { } mw)
        {
            return;
        }

        if (isPressed)
        {
            mw.TitlebarLeftDown = true;
            mw.TitlebarPressed = mw.TitlebarHovered;
            _wm.RequestManage();
            return;
        }

        var hovered = HoverAt(mw, _titlebarX, _titlebarY);
        mw.TitlebarHovered = hovered;
        mw.TitlebarLeftDown = false;
        var pressed = mw.TitlebarPressed;
        mw.TitlebarPressed = null;
        if (pressed is not null && pressed == hovered)
        {
            switch (pressed)
            {
                case TitlebarButton.Close:
                    var now = Environment.TickCount64;
                    if (_lastCloseClick is { } last && ReferenceEquals(last.Window, mw)
                        && now - last.At <= CloseDoubleClickMs)
                    {
                        _lastCloseClick = null;
                        mw.Events.Enqueue((WindowEventKind.Close, null));
                    }
                    else
                    {
                        _lastCloseClick = (mw, now);
                    }

                    break;
                case TitlebarButton.Hide:
                    _lastCloseClick = null;
                    mw.Events.Enqueue((WindowEventKind.Minimize, null));
                    break;
                case TitlebarButton.Maximize:
                    mw.Events.Enqueue((mw.Maximized ? WindowEventKind.Unmaximize : WindowEventKind.Maximize, null));
                    break;
            }
        }

        _wm.RequestManage();
    }

    private void OnDesktopButton(DesktopSurface desktop, uint button, bool isPressed)
    {
        if (!isPressed)
        {
            return;
        }

        var x = (int)_managerSurfaceX;
        var y = (int)_managerSurfaceY;
        if (button == InputCodes.BtnLeft)
        {
            if (desktop.IconAt(x, y) is { } icon)
            {
                var now = Environment.TickCount64;
                var doubleClick = _lastIconClick is { } last && ReferenceEquals(last.Window, icon)
                    && now - last.At <= CloseDoubleClickMs;
                _lastIconClick = (icon, now);
                EnterIconFocus(desktop, icon);
                if (doubleClick)
                {
                    _actions.Enqueue(WmAction.IconActivate);
                }
            }
            else
            {
                if (_menu is not null)
                {
                    _actions.Enqueue(WmAction.CloseWindowMenu);
                }

                _actions.Enqueue(WmAction.IconCancel);
                _actions.Enqueue(WmAction.ClearFocus);
            }

            _wm.RequestManage();
        }
        else if (button == InputCodes.BtnRight && desktop.IconAt(x, y) is null)
        {
            if (_menu is not null)
            {
                _actions.Enqueue(WmAction.CloseWindowMenu);
            }

            _pendingPointerMenu = (desktop.Output, new Point(x, y));
            _wm.RequestManage();
        }
    }

    private void EnterIconFocus(DesktopSurface desktop, ManagedWindow icon)
    {
        desktop.SelectedIcon = icon;
        _iconFocusOutput = desktop.Output;
        _actions.Enqueue(WmAction.ClearFocus);
        _actions.Enqueue(WmAction.SwitchModeIcons);
    }

    private void ExitIconFocus()
    {
        if (_iconFocusOutput is { } output && _desktops.TryGetValue(output, out var desktop))
        {
            desktop.SelectedIcon = null;
        }

        _iconFocusOutput = null;
        _mode = BindingMode.Default;
    }

    private DesktopSurface? IconFocusDesktop =>
        _iconFocusOutput is { } output && _desktops.TryGetValue(output, out var desktop) ? desktop : null;

    private void IconNavigate(int dx, int dy)
    {
        if (IconFocusDesktop is not { } desktop || desktop.IconCount == 0)
        {
            return;
        }

        var count = desktop.IconCount;
        var columns = Math.Max(desktop.Columns, 1);
        var current = desktop.SelectedIndex() ?? 0;
        int next;
        if (dy != 0)
        {
            var candidate = ((current / columns) + dy) * columns + (current % columns);
            next = candidate < 0 || candidate >= count ? current : candidate;
        }
        else
        {
            next = (((current + dx) % count) + count) % count;
        }

        if (desktop.IconWindowAt(next) is { } window)
        {
            desktop.SelectedIcon = window;
        }
    }

    private void IconActivate()
    {
        if (IconFocusDesktop is not { } desktop || desktop.SelectedIcon is not { } window)
        {
            ExitIconFocus();
            return;
        }

        ExitIconFocus();
        if (window.Window.IsClosed)
        {
            return;
        }

        window.Hidden = false;
        window.Window.Show();
        Focus(window);
    }

    private void ValidateIconFocus()
    {
        if (_menu is { } menu && menu.Output.IsRemoved)
        {
            CloseWindowMenu();
        }

        if (_iconFocusOutput is null)
        {
            return;
        }

        if (IconFocusDesktop is not { Output.IsRemoved: false, SelectedIcon: { Hidden: true } icon }
            || icon.Window.IsClosed)
        {
            ExitIconFocus();
        }
    }

    private void HandleWindowMenuCycle(bool byApp)
    {
        if (_compositor is null || _shm is null || _layerShell is null)
        {
            return;
        }

        if (_menuMode == MenuMode.AltTab && _menu is { } menu)
        {
            if (!byApp || MenuMatchesFocusedApp(menu))
            {
                if (menu.SelectNext() && HoveredWindow(menu) is { } preview)
                {
                    PreviewAltTab(preview);
                }

                return;
            }

            CloseWindowMenu();
        }
        else if (_menu is not null)
        {
            CloseWindowMenu();
        }

        OpenAltTabMenu(byApp);
    }

    private void OpenAltTabMenu(bool byApp)
    {
        var output = OutputForMenu();
        if (output is null or { IsRemoved: true })
        {
            return;
        }

        string? header;
        List<MenuItemEntry> items;
        if (byApp)
        {
            var appId = _focusStack.Focused?.Window.AppId;
            if (string.IsNullOrEmpty(appId))
            {
                return;
            }

            items = CollectMenuItems(appId);
            if (items.Count < 2)
            {
                return;
            }

            header = appId;
        }
        else
        {
            items = CollectMenuItems(null);
            if (items.Count == 0)
            {
                return;
            }

            header = "Windows";
        }

        if (_layerShell is null || _scales?.ProxyForName(output.WlOutputName) is not { } outputProxy)
        {
            return;
        }

        var menu = new MenuSurface(
            _compositor!, _shm!, _layerShell, outputProxy, output, _wm, items, header,
            _scales?.ScaleForName(output.WlOutputName) ?? 1);
        var area = output.Dimensions;
        menu.Origin = new Point(
            Math.Max((area.Width - menu.SurfaceSize.Width) / 2, 0),
            Math.Max((area.Height - menu.SurfaceSize.Height) / 2, 0));
        menu.ApplyPosition();
        _menu = menu;
        _menuMode = MenuMode.AltTab;
        BeginAltTab();
        menu.SelectWindow(_focusStack.Focused);
        menu.SelectNext();
        if (HoveredWindow(menu) is { } preview)
        {
            PreviewAltTab(preview);
        }
    }

    private void OpenPendingPointerMenu()
    {
        if (_pendingPointerMenu is not { } pending)
        {
            return;
        }

        _pendingPointerMenu = null;
        if (_compositor is null || _shm is null || _menu is not null || pending.Output.IsRemoved)
        {
            return;
        }

        var items = CollectMenuItems(null);
        if (items.Count == 0)
        {
            return;
        }

        if (_layerShell is null || _scales?.ProxyForName(pending.Output.WlOutputName) is not { } outputProxy)
        {
            return;
        }

        var menu = new MenuSurface(
            _compositor, _shm, _layerShell, outputProxy, pending.Output, _wm, items, null,
            _scales?.ScaleForName(pending.Output.WlOutputName) ?? 1);

        var anchor = menu.PointerAnchor;
        var area = pending.Output.Dimensions;
        var x = Math.Max(pending.Position.X - anchor.X, 0);
        var y = Math.Max(pending.Position.Y - anchor.Y, 0);
        if (x + menu.SurfaceSize.Width > area.Width)
        {
            x = Math.Max(area.Width - menu.SurfaceSize.Width, 0);
        }

        if (y + menu.SurfaceSize.Height > area.Height)
        {
            y = Math.Max(area.Height - menu.SurfaceSize.Height, 0);
        }

        menu.Origin = new Point(x, y);
        menu.ApplyPosition();
        menu.UpdateHover(pending.Position.X - x, pending.Position.Y - y);
        _menu = menu;
        _menuMode = MenuMode.Pointer;
    }

    private void ActivateMenuHovered()
    {
        if (_menu is not { Hovered: { } index } menu)
        {
            return;
        }

        var target = index < menu.Items.Count ? menu.Items[index].Window : null;
        ClearAltTabState();
        DisposeMenu();
        if (target is null || target.Window.IsClosed)
        {
            return;
        }

        if (target.Hidden)
        {
            target.Hidden = false;
            target.Window.Show();
        }

        Focus(target);
    }

    private void CloseWindowMenu()
    {
        if (_menuMode == MenuMode.AltTab)
        {
            RestoreAltTabState();
        }
        else
        {
            ClearAltTabState();
        }

        DisposeMenu();
    }

    private void DisposeMenu()
    {
        _menu?.Dispose();
        _menu = null;
        _menuMode = null;
        _shield?.Dispose();
        _shield = null;
    }

    private void BeginAltTab()
    {
        if (_altTabStack is not null)
        {
            return;
        }

        _altTabStack = [.. _focusStack];
        _altTabFocused = _focusStack.Focused;
        _altTabPreview = null;
        _altTabPreviewWasHidden = false;
    }

    private void PreviewAltTab(ManagedWindow mw)
    {
        if (_menuMode != MenuMode.AltTab)
        {
            return;
        }

        BeginAltTab();
        if (ReferenceEquals(_altTabPreview, mw))
        {
            return;
        }

        if (_altTabPreview is { } previous && _altTabPreviewWasHidden && !previous.Window.IsClosed)
        {
            previous.Hidden = true;
            previous.Window.Hide();
        }

        _altTabPreviewWasHidden = false;
        if (mw.Window.IsClosed)
        {
            return;
        }

        if (mw.Hidden)
        {
            _altTabPreviewWasHidden = true;
            mw.Hidden = false;
            mw.Window.Show();
        }

        FocusPreviewVisual(mw);
        _altTabPreview = mw;
    }

    private void FocusPreviewVisual(ManagedWindow mw)
    {
        _focusStack.Focused = mw;
        if (_currentSeat is { IsRemoved: false } seat)
        {
            seat.ClearFocus();
        }
    }

    private void RestoreAltTabState()
    {
        if (_altTabPreview is { } previous && _altTabPreviewWasHidden && !previous.Window.IsClosed)
        {
            previous.Hidden = true;
            previous.Window.Hide();
        }

        _altTabPreviewWasHidden = false;
        _altTabPreview = null;
        _pendingStackRestore = _altTabStack;

        if (_altTabFocused is { } former && !former.Window.IsClosed)
        {
            _focusStack.Focused = former;
            if (_currentSeat is { IsRemoved: false } seat && _wm.LayerShell?.HasExclusiveFocus(seat) != true)
            {
                seat.FocusWindow(former.Window);
            }
        }

        ClearAltTabState();
    }

    private void RestoreStackOrder(IReadOnlyList<ManagedWindow> order)
    {
        for (var i = order.Count - 1; i >= 0; i--)
        {
            if (!order[i].Window.IsClosed)
            {
                order[i].Window.Node.PlaceTop();
            }
        }
    }

    private void ClearAltTabState()
    {
        _altTabStack = null;
        _altTabFocused = null;
        _altTabPreview = null;
        _altTabPreviewWasHidden = false;
    }

    private ManagedWindow? HoveredWindow(MenuSurface menu) =>
        menu.Hovered is { } index && index < menu.Items.Count ? menu.Items[index].Window : null;

    private bool MenuMatchesFocusedApp(MenuSurface menu)
    {
        var appId = _focusStack.Focused?.Window.AppId;
        if (string.IsNullOrEmpty(appId) || menu.Items.Count == 0)
        {
            return false;
        }

        foreach (var item in menu.Items)
        {
            if (item.Window.Window.AppId != appId)
            {
                return false;
            }
        }

        return true;
    }

    private WmOutput? OutputForMenu()
    {
        if (_focusStack.Focused is { } focused)
        {
            var output = OutputForRect(focused) ?? focused.Output;
            if (output is { IsRemoved: false })
            {
                return output;
            }
        }

        return _currentOutput;
    }

    private List<MenuItemEntry> CollectMenuItems(string? appId)
    {
        var items = new List<MenuItemEntry>();
        var seen = new HashSet<ManagedWindow>();
        foreach (var mw in _focusStack)
        {
            if (!mw.Window.IsClosed && (appId is null || mw.Window.AppId == appId) && seen.Add(mw))
            {
                items.Add(MenuItemFor(mw));
            }
        }

        foreach (var mw in _windows)
        {
            if (!mw.Window.IsClosed && (appId is null || mw.Window.AppId == appId) && seen.Add(mw))
            {
                items.Add(MenuItemFor(mw));
            }
        }

        return items;
    }

    private MenuItemEntry MenuItemFor(ManagedWindow mw) => new(
        mw,
        TitleFor(mw),
        mw.Hidden,
        ReferenceEquals(mw, _focusStack.Focused));

    private static string TitleFor(ManagedWindow mw) =>
        mw.Window.Title is { Length: > 0 } title ? title : mw.Window.AppId ?? "Window";

    private void RenderDesktops()
    {
        foreach (var desktop in _desktops.Values)
        {
            var output = desktop.Output;
            if (output.IsRemoved)
            {
                continue;
            }

            var scale = _scales?.ScaleForName(output.WlOutputName) ?? 1;
            if (_wallpapers.TryGetValue(output, out var wallpaper) && wallpaper.Render(scale))
            {
                wallpaper.Commit();
            }

            var icons = CollectMinimizedIcons(output, scale);
            if (desktop.Render(icons, scale))
            {
                desktop.Commit();
            }
        }
    }

    private List<DesktopIcon> CollectMinimizedIcons(WmOutput output, int scale)
    {
        var entries = new List<(ulong Seq, ManagedWindow Window)>();
        foreach (var mw in _windows)
        {
            if (mw.Hidden && !mw.Window.IsClosed && ReferenceEquals(mw.Output, output))
            {
                entries.Add((mw.MinimizeSeq, mw));
            }
        }

        entries.Sort(static (a, b) => a.Seq.CompareTo(b.Seq));
        var icons = new List<DesktopIcon>(entries.Count);
        foreach (var (_, mw) in entries)
        {
            var image = mw.Window.AppId is { Length: > 0 } appId
                ? _icons.Load(appId, DesktopSurface.IconSize * scale)
                : null;
            icons.Add(new DesktopIcon(mw, TitleFor(mw), image));
        }

        return icons;
    }

    private void RenderMenuAndShield()
    {
        if (_menuMode == MenuMode.AltTab && _menu is { } forShield
            && _compositor is not null && _shm is not null && _layerShell is not null
            && !forShield.Output.IsRemoved)
        {
            if (_shield is null && _scales?.ProxyForName(forShield.Output.WlOutputName) is { } proxy)
            {
                _shield = new ShieldSurface(_compositor, _shm, _layerShell, proxy, forShield.Output, _wm);
            }

            _shield?.Render(_scales?.ScaleForName(forShield.Output.WlOutputName) ?? 1);
        }

        if (_menu is { } menu && !menu.Output.IsRemoved)
        {
            var scale = _scales?.ScaleForName(menu.Output.WlOutputName) ?? 1;
            if (menu.Render(scale))
            {
                menu.Commit();
            }
        }
    }

    private void UpdateDesktopCursor(DesktopSurface desktop, uint seatName, double x, double y)
    {
        _pointerInput.SetShape(seatName, desktop.IconAt((int)x, (int)y) is not null
            ? CursorShape.Pointer
            : CursorShape.Default);
    }

    private DesktopSurface? DesktopFor(uint surfaceId)
    {
        foreach (var desktop in _desktops.Values)
        {
            if (desktop.SurfaceId == surfaceId)
            {
                return desktop;
            }
        }

        foreach (var (output, wallpaper) in _wallpapers)
        {
            if (wallpaper.SurfaceId == surfaceId && _desktops.TryGetValue(output, out var desktop))
            {
                return desktop;
            }
        }

        return null;
    }

    private ManagedWindow? TitlebarWindowFor(uint surfaceId)
    {
        foreach (var mw in _windows)
        {
            if (mw.Titlebar?.SurfaceId == surfaceId)
            {
                return mw;
            }
        }

        return null;
    }

    private TitlebarButton? HoverAt(ManagedWindow mw, double x, double y) =>
        mw.Titlebar?.ButtonAt(
            mw.Width,
            Theme.BorderWidthFor(mw.FrameStyle),
            (int)x,
            (int)y,
            !mw.IsDialog,
            !mw.IsDialog && !mw.IsFixedSize);

    private void UpdateHoverCursor(ManagedWindow mw, uint seatName, double x, double y)
    {
        var bw = Theme.BorderWidthFor(mw.FrameStyle);
        var frame = new Rect(0, 0, mw.Width + (bw * 2), mw.Height + (bw * 2) + Theme.TitlebarHeight);
        var edges = mw.IsFixedSize
            ? Edges.None
            : EdgesNearBorder(frame, Theme.BorderWidth, new Point((int)x, (int)y));
        _pointerInput.SetShape(seatName, ShapeFor(edges));
    }

    private void UpdateCursor()
    {
        if (_menuMode == MenuMode.AltTab)
        {
            return;
        }

        if (_currentSeat is not { } seat)
        {
            return;
        }

        if (_drag is { Op.IsEnded: false } drag && drag.Edges != Edges.None)
        {
            _pointerInput.SetShape(seat.WlSeatName, ShapeFor(drag.Edges));
            return;
        }

        if (seat.PointerFocus is { } under && _byWindow.TryGetValue(under, out var mw) && !mw.IsFixedSize)
        {
            var edges = EdgesNearBorder(FrameRect(mw), Theme.BorderWidth, seat.PointerPosition);
            _pointerInput.SetShape(seat.WlSeatName, ShapeFor(edges));
        }
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
        Edges.Left | Edges.Right => CursorShape.EwResize,
        Edges.Top | Edges.Bottom => CursorShape.NsResize,
        _ => CursorShape.Default,
    };

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
            mw.Titlebar?.Invalidate();
            mw.Shadow?.Invalidate();
        }

        foreach (var desktop in _desktops.Values)
        {
            desktop.Invalidate();
        }

        foreach (var wallpaper in _wallpapers.Values)
        {
            wallpaper.Invalidate();
        }

        if (_menu is not null)
        {
            CloseWindowMenu();
        }
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
        if (_menuMode == MenuMode.AltTab
            && action is not (WmAction.WindowMenuCycle or WmAction.WindowMenuCycleApp
                or WmAction.WindowMenuCancel or WmAction.WindowMenuCommit))
        {
            return;
        }

        switch (action)
        {
            case WmAction.Close:
                _focusStack.Focused?.Window.Close();
                break;
            case WmAction.SpawnTerminal:
                Spawn(_config.TerminalCommand);
                break;
            case WmAction.SpawnLauncher:
                Spawn(_config.LauncherCommand);
                break;
            case WmAction.SpawnLock:
                Spawn(_config.LockCommand);
                break;
            case WmAction.HideFocused:
                if (_focusStack.Focused is { } toHide)
                {
                    Hide(toHide);
                }

                break;
            case WmAction.SmartHideFocused:
                SmartHide();
                break;
            case WmAction.MaximizeFocused:
                if (_focusStack.Focused is { } toMaximize)
                {
                    Maximize(toMaximize);
                }

                break;
            case WmAction.SnapLeft:
                if (_focusStack.Focused is { } toSnapLeft)
                {
                    SmartSnapHalf(toSnapLeft, SnapState.Left);
                }

                break;
            case WmAction.SnapRight:
                if (_focusStack.Focused is { } toSnapRight)
                {
                    SmartSnapHalf(toSnapRight, SnapState.Right);
                }

                break;
            case WmAction.SendToNextOutput:
                SendToOutput(1);
                break;
            case WmAction.SendToPreviousOutput:
                SendToOutput(-1);
                break;
            case WmAction.ToggleFullscreen:
                ToggleFullscreen();
                break;
            case WmAction.ClearFocus:
                ClearFocus();
                break;
            case WmAction.RestoreFocus:
                RestoreFocusFromStack();
                break;
            case WmAction.PointerMove:
                StartPointerMove();
                break;
            case WmAction.PointerResize:
                StartPointerResize();
                break;
            case WmAction.WindowMenuCycle:
                HandleWindowMenuCycle(byApp: false);
                break;
            case WmAction.WindowMenuCycleApp:
                HandleWindowMenuCycle(byApp: true);
                break;
            case WmAction.WindowMenuCommit:
                if (_menuMode == MenuMode.AltTab)
                {
                    if (_menu?.Hovered is not null)
                    {
                        ActivateMenuHovered();
                    }
                    else
                    {
                        CloseWindowMenu();
                    }
                }

                break;
            case WmAction.WindowMenuCancel:
                if (_menuMode == MenuMode.AltTab)
                {
                    CloseWindowMenu();
                }

                break;
            case WmAction.ActivateMenuHovered:
                if (_menuMode == MenuMode.Pointer && _menu?.Hovered is not null)
                {
                    ActivateMenuHovered();
                }

                break;
            case WmAction.CloseWindowMenu:
                CloseWindowMenu();
                break;
            case WmAction.IconSelectNext:
                IconNavigate(1, 0);
                break;
            case WmAction.IconSelectPrevious:
                IconNavigate(-1, 0);
                break;
            case WmAction.IconSelectUp:
                IconNavigate(0, -1);
                break;
            case WmAction.IconSelectDown:
                IconNavigate(0, 1);
                break;
            case WmAction.IconActivate:
                IconActivate();
                break;
            case WmAction.IconCancel:
                ExitIconFocus();
                break;
            case WmAction.SwitchModeIcons:
                _mode = BindingMode.DesktopIcons;
                break;
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
                ApplyChrome(mw);
                mw.ProposePreferred();
                Focus(mw);
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
                    ExitFullscreen(mw, restore: true);
                }

                break;
            case WindowEventKind.Maximize:
                Maximize(mw);
                break;
            case WindowEventKind.Unmaximize:
                Unmaximize(mw);
                break;
            case WindowEventKind.Minimize:
                Hide(mw);
                break;
        }
    }

    private void Focus(ManagedWindow mw) => _focusStack.Focus(mw);

    private void DemoteOtherFullscreen(ManagedWindow mw)
    {
        foreach (var other in _windows)
        {
            if (!ReferenceEquals(other, mw) && other.FullscreenOutput is not null)
            {
                ExitFullscreen(other, restore: true);
            }
        }
    }

    private void FollowWindowOutput(ManagedWindow mw)
    {
        var output = OutputForRect(mw) ?? mw.Output;
        if (output is not null)
        {
            SetWindowOutput(mw, output);
        }
    }

    private void ClearFocus() => _focusStack.ClearFocus();

    private void RestoreFocusFromStack() => _focusStack.RestoreFromStack(static mw => !mw.Hidden);

    private void EnterFullscreen(ManagedWindow mw, WmOutput output)
    {
        if (mw.Width <= 0)
        {
            var area = WmOutputPolicy.UsableArea(output);
            mw.PreFullscreen = area.IsEmpty
                ? new Rect(area.X, area.Y, 800, 600)
                : new Rect(
                    area.X + (area.Width / 4),
                    area.Y + (area.Height / 4),
                    Math.Max(area.Width / 2, 1),
                    Math.Max(area.Height / 2, 1));
        }
        else
        {
            mw.PreFullscreen ??= mw.ContentRect;
        }

        mw.Window.Fullscreen(output);
        mw.Window.InformFullscreen(true);
        mw.FullscreenOutput = output;
    }

    private static void ExitFullscreen(ManagedWindow mw, bool restore)
    {
        mw.Window.ExitFullscreen();
        mw.Window.InformFullscreen(false);
        mw.FullscreenOutput = null;

        if (restore && mw.PreFullscreen is { Width: > 0, Height: > 0 } saved)
        {
            mw.SetPosition(saved.X, saved.Y);
            mw.Propose(saved.Width, saved.Height);
        }
    }

    private void ToggleFullscreen()
    {
        if (_focusStack.Focused is not { } mw)
        {
            return;
        }

        if (mw.FullscreenOutput is null)
        {
            if (_currentOutput is { IsRemoved: false } output)
            {
                EnterFullscreen(mw, output);
            }
        }
        else
        {
            ExitFullscreen(mw, restore: true);
        }
    }

    private void Maximize(ManagedWindow mw)
    {
        UpdateOutputFromRect(mw);
        var output = OutputForRect(mw) ?? mw.Output ?? _currentOutput;
        if (output is null or { IsRemoved: true })
        {
            return;
        }

        var area = WmOutputPolicy.UsableArea(output);
        if (area.IsEmpty)
        {
            return;
        }

        if (!mw.Maximized && mw.PreSnap is null)
        {
            mw.PreSnap = mw.ContentRect;
        }

        var (bw, th) = Insets(mw);
        mw.Snap = SnapState.Maximized;
        mw.SetPosition(area.X + bw, area.Y + bw + th - mw.SwallowTop);
        mw.Propose(
            Math.Max(area.Width - (bw * 2), 1),
            Math.Max(area.Height - (bw * 2) - th + mw.SwallowTop, 1));
        mw.Maximized = true;
        mw.Window.InformMaximized(true);
    }

    private static void Unmaximize(ManagedWindow mw)
    {
        mw.Maximized = false;
        if (mw.PreSnap is { } saved)
        {
            mw.PreSnap = null;
            mw.SetPosition(saved.X, saved.Y);
            mw.Propose(Math.Max(saved.Width, 1), Math.Max(saved.Height, 1));
        }

        mw.Snap = null;
        mw.Window.InformMaximized(false);
    }

    private void SmartSnapHalf(ManagedWindow mw, SnapState side)
    {
        UpdateOutputFromRect(mw);
        var output = OutputForRect(mw) ?? mw.Output ?? _currentOutput;
        if (output is null or { IsRemoved: true })
        {
            return;
        }

        var area = WmOutputPolicy.UsableArea(output);
        if (area.IsEmpty)
        {
            return;
        }

        if (mw.Snap == side)
        {
            return;
        }

        var opposite = side == SnapState.Left ? SnapState.Right : SnapState.Left;
        if (mw.Snap == opposite)
        {
            if (mw.PreSnap is { } restored)
            {
                mw.PreSnap = null;
                mw.SetPosition(restored.X, restored.Y);
                mw.Propose(restored.Width, restored.Height);
            }

            mw.Snap = null;
            return;
        }

        if (mw.Snap == SnapState.Maximized)
        {
            if (mw.PreSnap is { } saved)
            {
                mw.SetPosition(saved.X, saved.Y);
                mw.Propose(saved.Width, saved.Height);
            }

            mw.Maximized = false;
            mw.Window.InformMaximized(false);
            mw.Snap = null;
        }

        mw.PreSnap ??= mw.FullscreenOutput is not null
            ? mw.PreFullscreen ?? mw.ContentRect
            : mw.ContentRect;

        if (mw.FullscreenOutput is not null)
        {
            ExitFullscreen(mw, restore: false);
        }

        if (mw.Maximized)
        {
            mw.Maximized = false;
            mw.Window.InformMaximized(false);
        }

        var leftWidth = area.Width / 2;
        var (sideWidth, sideX) = side == SnapState.Left
            ? (leftWidth, area.X)
            : (area.Width - leftWidth, area.X + leftWidth);

        var (bw, th) = Insets(mw);
        mw.SetPosition(sideX + bw, area.Y + bw + th - mw.SwallowTop);
        mw.Propose(
            Math.Max(sideWidth - (bw * 2), 1),
            Math.Max(area.Height - (bw * 2) - th + mw.SwallowTop, 1));
        mw.Snap = side;
    }

    private void SmartHide()
    {
        if (_focusStack.Focused is not { } mw)
        {
            return;
        }

        if (mw.FullscreenOutput is not null)
        {
            ExitFullscreen(mw, restore: true);
        }
        else if (mw.Maximized)
        {
            Unmaximize(mw);
        }
        else
        {
            Hide(mw);
        }
    }

    private void Hide(ManagedWindow mw)
    {
        var wasFocused = ReferenceEquals(_focusStack.Focused, mw);
        mw.MinimizeSeq = _nextMinimizeSeq++;
        mw.Hidden = true;
        mw.Window.Hide();
        if (!wasFocused)
        {
            return;
        }

        var output = _currentOutput is { IsRemoved: false } ? _currentOutput : mw.Output;
        if (output is null)
        {
            ClearFocus();
            return;
        }

        ManagedWindow? next = null;
        foreach (var candidate in _focusStack)
        {
            if (IsVisibleOn(candidate, output))
            {
                next = candidate;
                break;
            }
        }

        if (next is null)
        {
            foreach (var candidate in _windows)
            {
                if (IsVisibleOn(candidate, output))
                {
                    next = candidate;
                    break;
                }
            }
        }

        if (next is null)
        {
            ClearFocus();
        }
        else
        {
            Focus(next);
        }
    }

    private void SendToOutput(int direction)
    {
        if (_focusStack.Focused is not { } mw || _outputs.Count <= 1)
        {
            return;
        }

        var current = mw.Output ?? _currentOutput ?? _outputs[0];
        var index = _outputs.IndexOf(current);
        if (index < 0)
        {
            index = 0;
        }

        var target = _outputs[(((index + direction) % _outputs.Count) + _outputs.Count) % _outputs.Count];
        if (ReferenceEquals(target, _outputs[index]))
        {
            return;
        }

        if (mw.FullscreenOutput is not null)
        {
            SetWindowOutput(mw, target);
            mw.Window.Fullscreen(target);
            mw.FullscreenOutput = target;
            return;
        }

        var oldArea = WmOutputPolicy.UsableArea(_outputs[index]);
        var newArea = WmOutputPolicy.UsableArea(target);
        if (oldArea.IsEmpty || newArea.IsEmpty)
        {
            SetWindowOutput(mw, target);
            return;
        }

        var forceResize = mw.Snap is not null || mw.Maximized;
        var needsResize = forceResize
            || Math.Max(mw.Width, 1) > newArea.Width
            || Math.Max(mw.Height, 1) > newArea.Height;
        var mapped = MapRect(mw.ContentRect, oldArea, newArea, needsResize);

        if (mw.Snap is not null && mw.PreSnap is { } saved)
        {
            mw.PreSnap = MapRect(saved, oldArea, newArea, resize: true);
        }

        SetWindowOutput(mw, target);
        mw.SetPosition(mapped.X, mapped.Y);
        if (needsResize)
        {
            mw.Propose(mapped.Width, mapped.Height);
        }
    }

    private static Rect MapRect(Rect rect, Rect from, Rect to, bool resize)
    {
        var width = Math.Max(rect.Width, 1);
        var height = Math.Max(rect.Height, 1);
        var newWidth = resize ? (int)Math.Round(width / (double)from.Width * to.Width) : width;
        var newHeight = resize ? (int)Math.Round(height / (double)from.Height * to.Height) : height;
        newWidth = Math.Clamp(newWidth, 1, to.Width);
        newHeight = Math.Clamp(newHeight, 1, to.Height);

        var relX = (rect.X - from.X) / (double)from.Width;
        var relY = (rect.Y - from.Y) / (double)from.Height;
        var newX = to.X + (int)Math.Round(relX * to.Width);
        var newY = to.Y + (int)Math.Round(relY * to.Height);
        newX = Math.Clamp(newX, to.X, to.X + Math.Max(to.Width - newWidth, 0));
        newY = Math.Clamp(newY, to.Y, to.Y + Math.Max(to.Height - newHeight, 0));
        return new Rect(newX, newY, newWidth, newHeight);
    }

    private void ReassociateOutputs()
    {
        foreach (var mw in _windows)
        {
            UpdateOutputFromRect(mw);
        }
    }

    private void UpdateOutputFromRect(ManagedWindow mw)
    {
        var output = OutputForRect(mw);
        if (output is not null && !ReferenceEquals(output, mw.Output))
        {
            SetWindowOutput(mw, output);
        }
    }

    private void SetWindowOutput(ManagedWindow mw, WmOutput output)
    {
        mw.Output = output;
        if (ReferenceEquals(_focusStack.Focused, mw))
        {
            _currentOutput = output;
            _lastFocusedOutput = output;
            if (_wm.LayerShell is not null && !output.IsRemoved)
            {
                output.SetDefaultForLayerSurfaces();
            }
        }
    }

    private WmOutput? OutputForRect(ManagedWindow mw)
    {
        if (mw.PositionUndefined || mw.Width <= 0 || mw.Height <= 0)
        {
            return null;
        }

        var rect = mw.ContentRect;
        WmOutput? best = null;
        long bestArea = 0;
        foreach (var output in _outputs)
        {
            var overlap = rect.Intersect(output.Area);
            if (overlap.IsEmpty)
            {
                continue;
            }

            var overlapArea = (long)overlap.Width * overlap.Height;
            if (overlapArea > bestArea)
            {
                bestArea = overlapArea;
                best = output;
            }
        }

        return best;
    }

    private static bool IsVisibleOn(ManagedWindow mw, WmOutput output)
    {
        if (mw.Hidden || mw.Window.IsClosed)
        {
            return false;
        }

        return mw.Output is null || ReferenceEquals(mw.Output, output);
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
            var output = mw.Output ?? parent.Output;
            if (output is not null)
            {
                var area = WmOutputPolicy.UsableArea(output);
                if (!area.IsEmpty)
                {
                    var minX = area.X + Theme.BorderWidth;
                    var minY = area.Y + Theme.BorderWidth + Theme.TitlebarHeight;
                    var maxX = Math.Max(area.Right - Theme.BorderWidth - mw.Width, minX);
                    var maxY = Math.Max(area.Bottom - Theme.BorderWidth - mw.Height, minY);
                    cx = Math.Clamp(cx, minX, maxX);
                    cy = Math.Clamp(cy, minY, maxY);
                }
            }

            mw.SetPosition(cx, cy);
        }

        foreach (var output in _outputs)
        {
            _cascade.Clear();
            foreach (var mw in _windows)
            {
                if (!mw.PositionUndefined)
                {
                    continue;
                }

                var target = mw.Output ?? PreferredOutputFor(mw);
                if (!ReferenceEquals(target, output) || !IsVisibleOn(mw, output))
                {
                    continue;
                }

                _cascade.Add(mw);
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

            var targetWidth = Math.Max(area.Width / 2, 1);
            var targetHeight = Math.Max(area.Height / 2, 1);
            var padX = CascadePadding + Theme.BorderWidth;
            var padY = CascadePadding + Theme.BorderWidth + Theme.TitlebarHeight;
            var startX = area.X + padX;
            var startY = area.Y + padY;
            var endX = Math.Max(area.Right - targetWidth - padX, startX);
            var endY = Math.Max(area.Bottom - targetHeight - padY, startY);

            var step = Math.Max(padY, 1);
            var wrap = Math.Max((Math.Min(endX - startX, endY - startY) / step) + 1, 1);
            var next = _cascadeSteps.GetValueOrDefault(output);
            for (var i = 0; i < _cascade.Count; i++)
            {
                var k = (next + i) % wrap;
                _cascade[i].SetPosition(startX + (k * step), startY + (k * step));
            }

            _cascadeSteps[output] = (next + _cascade.Count) % wrap;
        }
    }

    private readonly Dictionary<WmOutput, int> _cascadeSteps = [];

    private WmOutput? PreferredOutputFor(ManagedWindow mw)
    {
        if (ParentOf(mw) is { Hidden: false, Output: { } parentOutput })
        {
            return parentOutput;
        }

        return _focusStack.Focused?.Output ?? _lastFocusedOutput ?? _currentOutput;
    }

    private void Restack()
    {
        if (_focusStack.Focused is { } focused && !focused.Window.IsClosed)
        {
            RootAncestor(focused).Window.Node.PlaceTop();
        }

        _restack.Clear();
        foreach (var mw in _windows)
        {
            if (ParentOf(mw) is { } parent)
            {
                _restack.Add((mw, parent, ChainDepth(mw)));
            }
        }

        _restack.Sort(static (a, b) => a.Depth.CompareTo(b.Depth));
        foreach (var (child, parent, _) in _restack)
        {
            child.Window.Node.PlaceAbove(parent.Window.Node);
        }
    }

    private ManagedWindow RootAncestor(ManagedWindow mw)
    {
        var current = mw;
        for (var i = 0; i < 64 && ParentOf(current) is { } parent; i++)
        {
            current = parent;
        }

        return current;
    }

    private int ChainDepth(ManagedWindow mw)
    {
        var depth = 0;
        var current = mw;
        for (var i = 0; i < 64 && ParentOf(current) is { } parent; i++)
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    private ManagedWindow? ParentOf(ManagedWindow mw) =>
        mw.Window.Parent is { } parent && _byWindow.TryGetValue(parent, out var managed) ? managed : null;

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

        _log.Debug($"manage: {context.Windows.Count} window(s), {context.Outputs.Count} output(s), {context.NewWindows.Count} new, {context.ClosedWindows.Count} closed");
        foreach (var mw in _windows)
        {
            _log.Debug($"  {(ReferenceEquals(mw, _focusStack.Focused) ? '*' : ' ')} '{(mw.Window.AppId ?? "?")}' at {mw.X},{mw.Y} {mw.Width}x{mw.Height}{((mw.Hidden ? " hidden" : string.Empty) +
                (mw.FullscreenOutput is not null ? " fullscreen" : string.Empty) +
                (mw.Snap is { } snap ? $" snap={snap}" : string.Empty))}");
        }
    }
}
