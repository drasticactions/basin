using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.River.Protocol;
using Basin.Shell.Xdg;
using Basin.XWayland;
using Wayland.Server;

namespace Basin.Shell.River;

public sealed class RiverWindowManager : IDisposable
{
    public const int Version = 5;

    private readonly WlServerDisplay _display;
    private readonly Basin.Scene.Scene _scene;
    private readonly XdgShell _xdgShell;
    private readonly OutputLayout _layout;
    private readonly SceneTree _windowTree;
    private readonly RiverWindowManagerOptions _options;
    private readonly ICompositorEventLoop _loop;
    private readonly WlGlobal _global;
    private readonly SceneTree _shellTree;
    private readonly SceneTree _popupTree;

    private readonly List<RiverWindow> _windows = [];
    private readonly List<RiverWindow> _closing = [];
    private readonly List<RiverOutput> _outputs = [];
    private readonly List<RiverSeat> _seats = [];
    private readonly Dictionary<RiverWindowV1Resource, RiverWindow> _windowsByResource = [];
    private readonly Dictionary<RiverOutputV1Resource, RiverOutput> _outputsByResource = [];
    private readonly Dictionary<RiverNodeV1Resource, RiverNode> _nodesByResource = [];
    private readonly List<RiverShellSurface> _shellSurfaces = [];
    private readonly List<RiverDecoration> _decorations = [];
    private readonly List<object> _heldCommits = [];

    private RiverWindowManagerV1Resource? _manager;
    private SequenceState _state = SequenceState.Idle;
    private SequenceDirt _dirt;
    private bool _idleQueued;
    private Transaction? _transaction;
    private bool _sessionLocked;
    private bool _sessionLockChanged;
    private bool _disposed;

    public RiverWindowManager(
        WlServerDisplay display,
        ICompositorEventLoop loop,
        Basin.Scene.Scene scene,
        SceneTree windowTree,
        XdgShell xdgShell,
        OutputLayout layout,
        IReadOnlyList<Basin.Seat.Seat> seats,
        RiverWindowManagerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(windowTree);
        ArgumentNullException.ThrowIfNull(xdgShell);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(seats);

        _display = display;
        _loop = loop;
        _scene = scene;
        _windowTree = windowTree;
        _xdgShell = xdgShell;
        _layout = layout;
        _options = options ?? new RiverWindowManagerOptions();

        foreach (var seat in seats)
        {
            _seats.Add(new RiverSeat(this, seat));
        }

        _shellTree = new SceneTree(windowTree.Parent ?? windowTree);
        _popupTree = new SceneTree(windowTree.Parent ?? windowTree);
        _popupTree.RaiseToTop();
        _xdgShell.NewToplevel += OnNewToplevel;
        _xdgShell.NewPopup += OnNewPopup;
        _global = display.CreateGlobal(RiverWindowManagerV1.Interface, Version, OnBind);
        _bindings = new RiverBindings(this, display);
        LayerShell = new RiverLayerShell(this, display);
        InputManager = new RiverInputManager(this, display);
        XkbConfig = new RiverXkbConfig(this, display);
        LibinputConfig = new RiverLibinputConfig(this, display);
    }

    public bool HasWindowManager => _manager is { IsDestroyed: false };

    public event Action? WindowManagerLost;

    public event Action? WindowManagerUnresponsive;

    public int DisplayedFullscreenCount { get; private set; }

    public int FrozenWindowCount
    {
        get
        {
            var frozen = 0;
            foreach (var window in _windows)
            {
                if (window.IsFrozen)
                {
                    frozen++;
                }
            }

            return frozen;
        }
    }

    public long ManageSequences { get; private set; }

    public long RenderSequences { get; private set; }

    public long UnresponsiveSequences { get; private set; }

    public RiverOutputArrangement Arrangement { get; } = new();

    public ForeignToplevelListManager? ForeignToplevels { get; set; }

    public Basin.Capabilities.IToplevelModel? Toplevels { get; set; }

    public XdgToplevelSource? ToplevelSource { get; set; }

    public XdgDecorationManager? Decorations
    {
        get => _xdgDecorations;
        set
        {
            if (_xdgDecorations is { } previous)
            {
                previous.PreferenceChanged -= OnDecorationPreference;
            }

            _xdgDecorations = value;
            if (value is not null)
            {
                value.PreferenceChanged += OnDecorationPreference;
            }
        }
    }

    private XdgDecorationManager? _xdgDecorations;

    private void OnDecorationPreference(XdgToplevelWindow toplevel, DecorationMode? preference)
    {
        if (_windows.Find(w => ReferenceEquals(w.Toplevel, toplevel)) is { } river)
        {
            river.ScheduleDecorationHint(HintFor(preference));
        }
    }

    internal RiverWindowV1.DecorationHint DecorationHintFor(XdgToplevelWindow toplevel) =>
        _xdgDecorations is { } decorations && decorations.TryGetPreference(toplevel, out var preference)
            ? HintFor(preference)
            : RiverWindowV1.DecorationHint.OnlySupportsCsd;

    private static RiverWindowV1.DecorationHint HintFor(DecorationMode? preference) => preference switch
    {
        DecorationMode.ClientSide => RiverWindowV1.DecorationHint.PrefersCsd,
        DecorationMode.ServerSide => RiverWindowV1.DecorationHint.PrefersSsd,
        _ => RiverWindowV1.DecorationHint.NoPreference,
    };

    public SessionLockManager? SessionLock
    {
        get => _sessionLock;
        set
        {
            if (_sessionLock is { } previous)
            {
                previous.Locked -= OnSessionLocked;
                previous.Unlocked -= OnSessionUnlocked;
            }

            _sessionLock = value;
            if (value is not null)
            {
                value.Locked += OnSessionLocked;
                value.Unlocked += OnSessionUnlocked;
                if (value.IsLocked)
                {
                    _sessionLocked = true;
                    _sessionLockChanged = true;
                }
            }
        }
    }

    private SessionLockManager? _sessionLock;

    private void OnSessionLocked()
    {
        _sessionLocked = true;
        _sessionLockChanged = true;
        MarkManageDirty();
    }

    private void OnSessionUnlocked()
    {
        _sessionLocked = false;
        _sessionLockChanged = true;
        MarkManageDirty();
    }

    public void AddOutput(OutputGlobal global)
    {
        ArgumentNullException.ThrowIfNull(global);
        var output = global.Output;
        if (_outputs.Exists(o => ReferenceEquals(o.Output, output)))
        {
            return;
        }

        var river = new RiverOutput(this, global);
        _outputs.Add(river);
        Arrangement.Add(output);

        output.Committed += river.NotifyOutputCommitted;
        MarkManageDirty();
    }

    public void RemoveOutput(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_outputs.Find(o => ReferenceEquals(o.Output, output)) is not { } river)
        {
            return;
        }

        output.Committed -= river.NotifyOutputCommitted;
        Arrangement.Remove(output);
        river.SendRemoved();
        LayerShell.OnOutputRemoved(river);
        _outputs.Remove(river);
        if (_backdrops.Remove(river, out var backdrop))
        {
            backdrop.Destroy();
        }

        foreach (var window in _windows)
        {
            if (ReferenceEquals(window.Requested.Fullscreen, river))
            {
                window.Requested.Fullscreen = null;
            }
        }

        MarkManageDirty();
    }

    public void Adopt(XWaylandWm xwayland)
    {
        ArgumentNullException.ThrowIfNull(xwayland);
        xwayland.WindowMapped += OnXWindowMapped;
    }

    public void NotifyInteraction(Basin.Seat.Seat seat, Surface surface)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(surface);
        if (SeatFor(seat) is not { } river)
        {
            return;
        }

        if (WindowOwning(surface) is { } window)
        {
            river.ReportInteraction(window);
        }
        else if (ShellSurfaceOwning(surface) is { } shell)
        {
            river.ReportInteraction(shell);
        }
    }

    public void NotifyPointerPosition(Basin.Seat.Seat seat, double x, double y)
    {
        if (SeatFor(seat) is not { } river)
        {
            return;
        }

        river.PointerPosition = new Point((int)x, (int)y);
        if (river.OperationActive)
        {
            river.PointerFocus = null;
            seat.Pointer.NotifyClearFocus();
            MarkManageDirty();
            return;
        }

        var hovered = _scene.SurfaceAt(x, y) is { Surface: { } surface } ? WindowOwning(surface) : null;
        if (ReferenceEquals(hovered, river.PointerFocus))
        {
            return;
        }

        river.PointerFocus = hovered;
        MarkManageDirty();
    }

    public void NotifyPointerReleased(Basin.Seat.Seat seat) => SeatFor(seat)?.ReportOperationReleased();

    public void RequestManage() => MarkManageDirty();

    public bool TryCaptureTrees(Surface surface, out SceneNode? content, out SceneNode? popups)
    {
        ArgumentNullException.ThrowIfNull(surface);
        foreach (var window in _windows)
        {
            if (ReferenceEquals(window.Surface, surface))
            {
                content = window.Tree;
                popups = window.PopupTree;
                return true;
            }
        }

        content = null;
        popups = null;
        return false;
    }

    public void SetCaptureSessions(XdgToplevelWindow window, int count)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_windows.Find(w => ReferenceEquals(w.Toplevel, window)) is { } river)
        {
            river.ScheduleCaptureSessions((uint)count);
            MarkManageDirty();
        }
    }

    public void SetCaptureSessions(IOutput output, int count)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (RiverOutputFor(output) is { } river && river.CaptureSessions != (uint)count)
        {
            river.CaptureSessions = (uint)count;
            MarkManageDirty();
        }
    }

    public void SetPresentationHint(XdgToplevelWindow window, PresentationMode hint)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windows.Find(w => ReferenceEquals(w.Toplevel, window)) is { } river)
        {
            river.SchedulePresentationHint((RiverOutputV1.PresentationMode)hint);
            MarkRenderDirty();
        }
    }

    public PresentationMode PresentationModeOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return RiverOutputFor(output) is { } river
            ? (PresentationMode)river.PresentationMode
            : PresentationMode.Vsync;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _xdgShell.NewToplevel -= OnNewToplevel;
        _xdgShell.NewPopup -= OnNewPopup;
        if (_xdgDecorations is { } xdgDecorations)
        {
            xdgDecorations.PreferenceChanged -= OnDecorationPreference;
        }

        if (_sessionLock is { } sessionLock)
        {
            sessionLock.Locked -= OnSessionLocked;
            sessionLock.Unlocked -= OnSessionUnlocked;
        }

        foreach (var output in _outputs)
        {
            output.Output.Committed -= output.NotifyOutputCommitted;
        }

        _bindings.Dispose();
        LayerShell.Dispose();
        InputManager.Dispose();
        XkbConfig.Dispose();
        LibinputConfig.Dispose();
        _transaction?.Dispose();
        _transaction = null;
        _watchdog?.Remove();
        _watchdog = null;
        foreach (var shell in _shellSurfaces.ToArray())
        {
            shell.Destroy();
        }

        foreach (var decoration in _decorations.ToArray())
        {
            decoration.Destroy();
        }

        foreach (var backdrop in _backdrops.Values)
        {
            backdrop.Destroy();
        }

        _backdrops.Clear();
        _shellTree.Destroy();
        foreach (var window in _windows)
        {
            window.DestroyPopups();
        }

        foreach (var closing in _closing)
        {
            closing.DestroyPopups();
        }

        _popupTree.Destroy();
        _windows.Clear();
        _closing.Clear();
        _outputs.Clear();
        _seats.Clear();
        _windowsByResource.Clear();
        _outputsByResource.Clear();
        _nodesByResource.Clear();
        _global.Dispose();
    }

    private readonly RiverBindings _bindings;

    public RiverLayerShell LayerShell { get; }

    public RiverInputManager InputManager { get; }

    public RiverXkbConfig XkbConfig { get; }

    public RiverLibinputConfig LibinputConfig { get; }

    internal IOutput? OutputForWlResource(Wayland.WlOutputResource? resource) =>
        resource is null ? null : OutputGlobal.FromResource(resource)?.Output;

    internal RiverOutput? RiverOutputFor(IOutput output) =>
        _outputs.Find(o => ReferenceEquals(o.Output, output));

    internal RiverSeat? RiverSeatFor(Basin.Seat.Seat seat) => SeatFor(seat);

    public bool HandleKey(Basin.Seat.Seat seat, uint key, bool pressed)
    {
        ArgumentNullException.ThrowIfNull(seat);
        if (SeatFor(seat) is not { } river || !_bindings.HandleKey(river, key, pressed))
        {
            return false;
        }

        seat.Keyboard.NotifyKeyConsumed(key, pressed);
        return true;
    }

    public void NotifyModifiers(Basin.Seat.Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        if (SeatFor(seat) is { } river)
        {
            _bindings.HandleModifiers(river);
        }
    }

    public bool HandlePointerButton(Basin.Seat.Seat seat, uint timeMs, uint button, bool pressed)
    {
        ArgumentNullException.ThrowIfNull(seat);
        if (SeatFor(seat) is not { } river)
        {
            return false;
        }

        if (river.OperationActive)
        {
            if (!pressed)
            {
                river.ReportOperationReleased();
            }

            seat.Pointer.NotifyButton(timeMs, button, pressed);
            seat.Pointer.NotifyFrame();
            return true;
        }

        return river.HandleButton(button, pressed, ModifiersOf(seat));
    }

    public bool HasPointerOperation(Basin.Seat.Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        return SeatFor(seat) is { OperationActive: true };
    }

    private static RiverSeatV1.Modifiers ModifiersOf(Basin.Seat.Seat seat)
    {
        if (seat.Keyboard.State is not { } state)
        {
            return RiverSeatV1.Modifiers.None;
        }

        var result = RiverSeatV1.Modifiers.None;
        if (state.IsModActive("Shift"))
        {
            result |= RiverSeatV1.Modifiers.Shift;
        }

        if (state.IsModActive("Control"))
        {
            result |= RiverSeatV1.Modifiers.Ctrl;
        }

        if (state.IsModActive("Mod1"))
        {
            result |= RiverSeatV1.Modifiers.Mod1;
        }

        if (state.IsModActive("Mod3"))
        {
            result |= RiverSeatV1.Modifiers.Mod3;
        }

        if (state.IsModActive("Mod4"))
        {
            result |= RiverSeatV1.Modifiers.Mod4;
        }

        if (state.IsModActive("Mod5"))
        {
            result |= RiverSeatV1.Modifiers.Mod5;
        }

        return result;
    }

    internal RiverSeat? ResolveSeat(RiverSeatV1Resource? resource) =>
        resource is null ? null : _seats.Find(s => ReferenceEquals(s.Resource, resource));

    public CompositorGlobal? Compositor { get; set; }

    internal void HoldNextCommit(object surface)
    {
        if (!_heldCommits.Contains(surface))
        {
            _heldCommits.Add(surface);
        }

        switch (surface)
        {
            case RiverShellSurface shell:
                shell.AwaitingCommit = true;
                shell.Surface.HoldNextCommit();
                break;
            case RiverDecoration decoration:
                decoration.AwaitingCommit = true;
                decoration.Surface.HoldNextCommit();
                break;
        }
    }

    internal void ForgetShellSurface(RiverShellSurface shell)
    {
        _shellSurfaces.Remove(shell);
        ReleaseHoldOn(shell, shell.Surface);
        MarkRenderDirty();
    }

    internal void ForgetDecoration(RiverDecoration decoration)
    {
        _decorations.Remove(decoration);
        ReleaseHoldOn(decoration, decoration.Surface);
        MarkRenderDirty();
    }

    private void ReleaseHoldOn(object holder, Surface surface)
    {
        if (_heldCommits.Remove(holder))
        {
            surface.ReleaseHeldCommits();
        }
    }

    internal void AddDecoration(RiverDecoration decoration) => _decorations.Add(decoration);

    internal RenderList<RiverNode> RenderOrder { get; } = new();

    internal RiverSeat? PrimarySeat => _seats.Count > 0 ? _seats[0] : null;

    internal bool EnsureWindowing() => SequenceGuard.EnsureWindowing(_manager, _state);

    internal bool EnsureRendering() => SequenceGuard.EnsureRendering(_manager, _state);

    internal RiverWindow? ResolveWindow(RiverWindowV1Resource? resource) =>
        resource is not null ? _windowsByResource.GetValueOrDefault(resource) : null;

    internal RiverOutput? ResolveOutput(RiverOutputV1Resource? resource) =>
        resource is not null ? _outputsByResource.GetValueOrDefault(resource) : null;

    internal RiverNode? ResolveNode(RiverNodeV1Resource? resource) =>
        resource is not null ? _nodesByResource.GetValueOrDefault(resource) : null;

    internal void RegisterNode(RiverNode node)
    {
        _nodesByResource[node.Resource] = node;
        RenderOrder.Add(node);
    }

    internal void ForgetNode(RiverNode node)
    {
        _nodesByResource.Remove(node.Resource);
        RenderOrder.Remove(node);
    }

    internal void ForgetOutputResource(RiverOutputV1Resource resource) =>
        _outputsByResource.Remove(resource);

    internal void ForgetWindowResource(RiverWindow window)
    {
        if (window.Resource is { } resource)
        {
            _windowsByResource.Remove(resource);
        }

        window.Resource = null;
        _closing.Remove(window);
    }

    internal RiverOutput? OutputUnderPointer()
    {
        if (PrimarySeat is not { } seat)
        {
            return _outputs.Count > 0 ? _outputs[0] : null;
        }

        var point = seat.PointerPosition;
        foreach (var output in _outputs)
        {
            var area = new Box(output.Position.X, output.Position.Y, output.Width, output.Height);
            if (area.Contains(point))
            {
                return output;
            }
        }

        return _outputs.Count > 0 ? _outputs[0] : null;
    }

    internal void MarkManageDirty()
    {
        _dirt |= SequenceDirt.Manage;
        ArmIdle();
    }

    internal void MarkRenderDirty()
    {
        _dirt |= SequenceDirt.Render;
        ArmIdle();
    }

    private void MarkManageDirtyLazy()
    {
        _dirt |= SequenceDirt.ManageLazy;
        ArmIdle();
    }

    private void ArmIdle()
    {
        if (_idleQueued || _disposed)
        {
            return;
        }

        _idleQueued = true;
        _runIdle ??= RunIdle;
        _loop.AddIdle(_runIdle);
    }

    private Action? _runIdle;

    private void RunIdle()
    {
        _idleQueued = false;
        StartSequenceIfIdle();
    }

    private void StartSequenceIfIdle()
    {
        if (_disposed || _state != SequenceState.Idle || _dirt == SequenceDirt.None)
        {
            return;
        }

        if (_dirt == SequenceDirt.Render)
        {
            _dirt = SequenceDirt.None;
            StartRender();
            return;
        }

        _dirt = SequenceDirt.None;
        StartManage();
    }

    private void StartManage()
    {
        ManageSequences++;
        _state = SequenceState.Manage;
        ArrangeOutputs();
        LayerShell.RefreshAreas();

        var version = ManagerVersion;
        if (_manager is { IsDestroyed: false } manager)
        {
            if (_sessionLockChanged)
            {
                _sessionLockChanged = false;
                if (_sessionLocked)
                {
                    manager.SendSessionLocked();
                }
                else
                {
                    manager.SendSessionUnlocked();
                }
            }

            foreach (var output in _outputs)
            {
                if (output.Resource is null)
                {
                    AnnounceOutput(manager, output);
                }

                output.SendChanges(version);
            }

            foreach (var window in _windows)
            {
                if (window.Resource is null)
                {
                    AnnounceWindow(manager, window);
                }

                window.SendChanges(version);
            }

            foreach (var closing in _closing)
            {
                closing.Resource?.SendClosed();
            }

            foreach (var seat in _seats)
            {
                if (seat.Resource is null)
                {
                    AnnounceSeat(manager, seat);
                }

                seat.SendChanges(version);
            }

            XkbConfig.SendKeyboardState(version);

            ArmUnresponsiveWatchdog();
            manager.SendManageStart();
            return;
        }

        FinishManage();
    }

    private void FinishManage()
    {
        if (_state != SequenceState.Manage)
        {
            return;
        }

        DisarmUnresponsiveWatchdog();
        _state = SequenceState.AwaitingConfigures;

        ApplySeatState();

        var transaction = new Transaction(_loop, _options.TransactionTimeoutMs);
        _transaction = transaction;
        foreach (var window in _windows)
        {
            window.ApplyWindowingState(transaction, IsFocused(window));
        }

        transaction.Completed += () =>
        {
            if (!ReferenceEquals(_transaction, transaction))
            {
                return;
            }

            _transaction = null;
            _loop.DeferDestroy(transaction);
            StartRender();
        };
        transaction.Seal();
    }

    private void ApplySeatState()
    {
        foreach (var seat in _seats)
        {
            if (seat.PendingWarp is { } warp)
            {
                seat.PointerPosition = ClampIntoOutputs(warp);
                WarpRequested?.Invoke(seat.Seat, seat.PointerPosition);
            }

            seat.BeginPendingOperation();

            if (_sessionLocked)
            {
                seat.ClearFocusRequest();
                continue;
            }

            var layerFocus = LayerShell.FocusFor(seat, out var layerSurface);
            if (layerFocus == LayerFocus.Exclusive)
            {
                if (layerSurface is not null)
                {
                    seat.Seat.Keyboard.NotifyEnter(layerSurface);
                }

                seat.ClearFocusRequest();
                continue;
            }

            var managerSetFocus = seat.RequestedFocus != FocusTarget.Unchanged;
            switch (seat.RequestedFocus)
            {
                case FocusTarget.Window when seat.RequestedFocusWindow is { } window:
                    seat.Seat.Keyboard.NotifyEnter(window.Surface);
                    _focused = window;
                    break;
                case FocusTarget.None:
                    seat.Seat.Keyboard.NotifyClearFocus();
                    _focused = null;
                    break;
            }

            seat.ClearFocusRequest();

            if (layerFocus != LayerFocus.NonExclusive)
            {
                continue;
            }

            if (managerSetFocus)
            {
                LayerShell.DropNonExclusiveFocus(seat);
            }
            else if (layerSurface is not null)
            {
                seat.Seat.Keyboard.NotifyEnter(layerSurface);
            }
        }
    }

    private RiverWindow? _focused;

    private bool IsFocused(RiverWindow window) => ReferenceEquals(_focused, window);

    private void StartRender()
    {
        RenderSequences++;
        _state = SequenceState.Render;

        if (_manager is { IsDestroyed: false } manager)
        {
            foreach (var window in _windows)
            {
                window.SendPresentationHintIfChanged(ManagerVersion);
                window.SendDimensionsIfChanged();
            }

            ArmUnresponsiveWatchdog();
            manager.SendRenderStart();
            return;
        }

        FinishRender();
    }

    private void FinishRender()
    {
        if (_state != SequenceState.Render)
        {
            return;
        }

        DisarmUnresponsiveWatchdog();
        _state = SequenceState.Idle;

        foreach (var window in _windows)
        {
            window.ApplyRenderState();
            window.ReportCaptureGeometry(ToplevelSource);
        }

        ApplyHeldCommits();
        ApplyShellSurfaces();
        ApplyDecorations();
        ApplyFullscreen();
        ProjectRenderOrder();

        foreach (var window in _windows)
        {
            window.ReleaseSnapshot(_loop);
        }

        foreach (var closing in _closing.ToArray())
        {
            closing.ReleaseSnapshot(_loop);
            closing.DestroyPopups();
            closing.Tree.Destroy();
        }

        _closing.Clear();

        if (_dirt != SequenceDirt.None)
        {
            ArmIdle();
        }
    }

    private void ApplyHeldCommits()
    {
        for (var i = _heldCommits.Count - 1; i >= 0; i--)
        {
            switch (_heldCommits[i])
            {
                case RiverShellSurface shell:
                    ApplyOne(shell.AwaitingCommit, shell.Surface, shell.PostMissingCommit);
                    shell.AwaitingCommit = false;
                    break;
                case RiverDecoration decoration:
                    ApplyOne(decoration.AwaitingCommit, decoration.Surface, decoration.PostMissingCommit);
                    decoration.AwaitingCommit = false;
                    break;
            }
        }

        _heldCommits.Clear();

        static void ApplyOne(bool missing, Surface surface, Action postError)
        {
            if (missing)
            {
                postError();
                return;
            }

            surface.ReleaseHeldCommits();
        }
    }

    private void ApplyFullscreen()
    {
        DisplayedFullscreenCount = 0;
        foreach (var output in _outputs)
        {
            RiverWindow? top = null;
            var topIndex = -1;
            foreach (var window in _windows)
            {
                if (!ReferenceEquals(window.Requested.Fullscreen, output) || !window.IsDisplayable)
                {
                    continue;
                }

                var index = window.Node is { } node ? RenderOrder.IndexOf(node) : -1;
                if (index >= topIndex)
                {
                    topIndex = index;
                    top = window;
                }
            }

            EnsureBackdrop(output, top is not null);

            foreach (var window in _windows)
            {
                if (!ReferenceEquals(window.Requested.Fullscreen, output))
                {
                    continue;
                }

                var displayed = ReferenceEquals(window, top);
                if (displayed)
                {
                    DisplayedFullscreenCount++;
                }

                window.SetFullscreenDisplayed(displayed, OutputBox(output));
            }
        }
    }

    private static Box OutputBox(RiverOutput output) =>
        new(output.Position.X, output.Position.Y, output.Width, output.Height);

    private void EnsureBackdrop(RiverOutput output, bool wanted)
    {
        if (!wanted)
        {
            if (_backdrops.TryGetValue(output, out var existing))
            {
                existing.Enabled = false;
            }

            return;
        }

        var box = OutputBox(output);
        if (!_backdrops.TryGetValue(output, out var backdrop))
        {
            backdrop = new SceneRect(_windowTree, box.Width, box.Height, new RenderColor(0f, 0f, 0f, 1f));
            _backdrops[output] = backdrop;
        }

        backdrop.Enabled = true;
        backdrop.Width = box.Width;
        backdrop.Height = box.Height;
        backdrop.SetPosition(box.X, box.Y);
        backdrop.LowerToBottom();
    }

    private readonly Dictionary<RiverOutput, SceneRect> _backdrops = [];

    private void ApplyShellSurfaces()
    {
        foreach (var shell in _shellSurfaces)
        {
            if (!shell.IsDestroyed && shell.Node?.RequestedPosition is { } position)
            {
                shell.Tree.SetPosition(position.X, position.Y);
            }
        }
    }

    private void ApplyDecorations()
    {
        foreach (var decoration in _decorations)
        {
            if (decoration.IsDestroyed)
            {
                continue;
            }

            decoration.Tree.SetPosition(decoration.Offset.X, decoration.Offset.Y);

            if (decoration.IsAbove)
            {
                decoration.Tree.RaiseToTop();
            }
            else
            {
                decoration.Tree.LowerToBottom();
            }
        }
    }

    private void ProjectRenderOrder()
    {
        foreach (var node in RenderOrder.Entries)
        {
            node.Tree()?.RaiseToTop();
        }

        _popupTree.RaiseToTop();
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (WindowForPopup(popup) is not { } window)
        {
            return;
        }

        var scene = new SceneSurface(window.PopupTree, popup.Surface);
        window.PopupScenes.Add(scene);

        void Place()
        {
            var chain = PopupChainOffset(popup);
            scene.Tree.SetPosition(
                chain.X + popup.SurfacePosition.X, chain.Y + popup.SurfacePosition.Y);
        }

        void Constrain()
        {
            var chain = PopupChainOffset(popup);
            var originX = window.Tree.X + chain.X;
            var originY = window.Tree.Y + chain.Y;
            if (_layout.OutputAt(originX, originY) is { } output)
            {
                var box = _layout.BoxOf(output);
                popup.Unconstrain(new Box(box.X - originX, box.Y - originY, box.Width, box.Height));
            }
        }

        Constrain();
        Place();
        popup.Xdg.Committed += Place;
        popup.GeometryChanged += Place;
        popup.Repositioned += Constrain;
        scene.Destroyed += () =>
        {
            popup.Xdg.Committed -= Place;
            popup.GeometryChanged -= Place;
            popup.Repositioned -= Constrain;
            window.PopupScenes.Remove(scene);
        };
        popup.Destroyed += scene.Destroy;
    }

    private RiverWindow? WindowForPopup(XdgPopupWindow popup)
    {
        var xdg = popup.Parent;
        while (xdg is not null)
        {
            if (xdg.Role is XdgPopupWindow parent)
            {
                xdg = parent.Parent;
                continue;
            }

            if (xdg.Role is XdgToplevelWindow toplevel)
            {
                foreach (var window in _windows)
                {
                    if (ReferenceEquals(window.Toplevel, toplevel))
                    {
                        return window;
                    }
                }
            }

            return null;
        }

        return null;
    }

    private static Point PopupChainOffset(XdgPopupWindow popup)
    {
        var x = 0;
        var y = 0;
        var xdg = popup.Parent;
        while (xdg?.Role is XdgPopupWindow parent)
        {
            x += parent.Geometry.X;
            y += parent.Geometry.Y;
            xdg = parent.Parent;
        }

        return new Point(x, y);
    }

    private uint ManagerVersion => _manager is { } manager ? manager.Version : Version;

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new RiverWindowManagerV1Resource(client, version, id);

        if (_manager is { IsDestroyed: false })
        {
            resource.SendUnavailable();
            return;
        }

        _manager = resource;
        resource.ManageFinish += (_, _) =>
        {
            if (_state != SequenceState.Manage)
            {
                resource.PostError(
                    (uint)RiverWindowManagerV1.Error.SequenceOrder,
                    "manage_finish outside a manage sequence");
                return;
            }

            FinishManage();
        };
        resource.RenderFinish += (_, _) =>
        {
            if (_state != SequenceState.Render)
            {
                resource.PostError(
                    (uint)RiverWindowManagerV1.Error.SequenceOrder,
                    "render_finish outside a render sequence");
                return;
            }

            FinishRender();
        };
        resource.ManageDirty += (_, _) => MarkManageDirtyLazy();
        resource.GetShellSurface += (_, e) =>
        {
            if (Compositor is null || e.Surface is null)
            {
                return;
            }

            var surface = Compositor.ResolveSurface(e.Surface);
            if (surface is null || !surface.CanSetRole(RiverShellSurface.RoleName) ||
                surface.Current.Buffer is not null || surface.Pending.Buffer is not null)
            {
                resource.PostError(
                    (uint)RiverWindowManagerV1.Error.Role,
                    "the surface already has a role or a buffer");
                return;
            }

            var shellResource = new RiverShellSurfaceV1Resource(resource.Client, resource.Version, e.Id);
            var shell = new RiverShellSurface(this, shellResource, surface, _shellTree);
            surface.TrySetRole(RiverShellSurface.RoleName, shell);
            _shellSurfaces.Add(shell);
            MarkRenderDirty();
        };
        resource.Stop += (_, _) =>
        {
            resource.SendFinished();
            DetachManager();
        };
        resource.ExitSession += (_, _) => ExitSessionRequested?.Invoke();
        resource.DestroyRequest += (_, _) => DetachManager();
        resource.Destroyed += (_, _) => DetachManager();

        foreach (var window in _windows)
        {
            window.ResetForNewManager();
        }

        foreach (var output in _outputs)
        {
            output.ResetForNewManager();
        }

        foreach (var seat in _seats)
        {
            seat.ResetForNewManager();
        }

        _bindings.ResetForNewManager();
        LayerShell.ResetForNewManager();
        InputManager.ResetForNewManager();
        XkbConfig.ResetForNewManager();
        LibinputConfig.ResetForNewManager();
        RenderOrder.Clear();
        _nodesByResource.Clear();
        _windowsByResource.Clear();
        _outputsByResource.Clear();
        MarkManageDirty();
    }

    public event Action? ExitSessionRequested;

    public event Action<Basin.Seat.Seat, Point>? WarpRequested;

    private void DetachManager()
    {
        if (_manager is null)
        {
            return;
        }

        _manager = null;
        DisarmUnresponsiveWatchdog();

        if (_state is SequenceState.Manage)
        {
            FinishManage();
        }
        else if (_state is SequenceState.Render)
        {
            FinishRender();
        }

        foreach (var window in _windows)
        {
            window.ResetForNewManager();
        }

        _windowsByResource.Clear();
        _outputsByResource.Clear();
        _nodesByResource.Clear();
        WindowManagerLost?.Invoke();
    }

    private void AnnounceWindow(RiverWindowManagerV1Resource manager, RiverWindow window)
    {
        var resource = new RiverWindowV1Resource(manager.Client, manager.Version, 0);
        window.Bind(resource);
        _windowsByResource[resource] = window;
        manager.SendWindow(resource);

        var identifier = IdentifierOf(window);
        window.ScheduleIdentity(PidOf(window), identifier);
    }

    private void AnnounceOutput(RiverWindowManagerV1Resource manager, RiverOutput output)
    {
        var resource = new RiverOutputV1Resource(manager.Client, manager.Version, 0);
        output.Bind(resource);
        _outputsByResource[resource] = output;
        manager.SendOutput(resource);
    }

    private void AnnounceSeat(RiverWindowManagerV1Resource manager, RiverSeat seat)
    {
        var resource = new RiverSeatV1Resource(manager.Client, manager.Version, 0);
        seat.Bind(resource);
        manager.SendSeat(resource);

        resource.SendWlSeat(seat.Seat.NameFor(resource.Client));
    }

    private Basin.Capabilities.ToplevelInfo[]? _toplevelScratch;

    private string? IdentifierOf(RiverWindow window)
    {
        if (ForeignToplevels is not { } list || Toplevels is not { } model)
        {
            return null;
        }

        _toplevelScratch ??= new Basin.Capabilities.ToplevelInfo[32];
        var count = model.Enumerate(_toplevelScratch);
        while (count < 0)
        {
            _toplevelScratch = new Basin.Capabilities.ToplevelInfo[_toplevelScratch.Length * 2];
            count = model.Enumerate(_toplevelScratch);
        }

        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(_toplevelScratch[i].Surface, window.Surface))
            {
                return list.IdentifierOf(_toplevelScratch[i].Id);
            }
        }

        return null;
    }

    private static int PidOf(RiverWindow window) =>
        window.Surface.Resource.Client.TryGetCredentials(out var credentials) ? credentials.Pid : 0;

    private void OnNewToplevel(XdgToplevelWindow toplevel)
    {
        toplevel.Xdg.Mapped += () =>
        {
            var tree = new SceneTree(_windowTree) { Enabled = false };
            var scene = new SceneSurface(tree, toplevel.Surface);
            var popupTree = new SceneTree(_popupTree) { Enabled = false };
            var window = new RiverWindow(this, toplevel, scene, tree, popupTree);
            _windows.Add(window);
            MarkManageDirty();
        };
        toplevel.Xdg.Unmapped += () => CloseWindow(w => ReferenceEquals(w.Toplevel, toplevel));
    }

    private void OnXWindowMapped(XWaylandWindow xwindow)
    {
        if (xwindow.Surface is not { } surface)
        {
            return;
        }

        var tree = new SceneTree(_windowTree) { Enabled = false };
        var scene = new SceneSurface(tree, surface);
        var popupTree = new SceneTree(_popupTree) { Enabled = false };
        var window = new RiverWindow(this, xwindow, scene, tree, popupTree);
        _windows.Add(window);
        xwindow.Unmapped += () => CloseWindow(w => ReferenceEquals(w.XWindow, xwindow));
        MarkManageDirty();
    }

    private void CloseWindow(Func<RiverWindow, bool> match)
    {
        if (_windows.Find(w => match(w)) is not { } window)
        {
            return;
        }

        _windows.Remove(window);
        window.BeginClosing();
        _closing.Add(window);
        if (window.Node is { } node)
        {
            RenderOrder.Remove(node);
        }

        MarkManageDirty();
    }

    private RiverWindow? WindowOwning(Surface surface)
    {
        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            if (candidate.RoleObject is XdgPopupWindow popup && WindowForPopup(popup) is { } popupWindow)
            {
                return popupWindow;
            }

            foreach (var window in _windows)
            {
                if (candidate == window.Surface)
                {
                    return window;
                }
            }

            foreach (var decoration in _decorations)
            {
                if (candidate == decoration.Surface && !decoration.IsDestroyed)
                {
                    return decoration.Window;
                }
            }
        }

        return null;
    }

    private RiverShellSurface? ShellSurfaceOwning(Surface surface)
    {
        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            foreach (var shell in _shellSurfaces)
            {
                if (candidate == shell.Surface && !shell.IsDestroyed)
                {
                    return shell;
                }
            }
        }

        return null;
    }

    private RiverSeat? SeatFor(Basin.Seat.Seat seat) => _seats.Find(s => ReferenceEquals(s.Seat, seat));

    private void ArrangeOutputs()
    {
        Arrangement.Arrange(_layout);
        foreach (var output in _outputs)
        {
            var box = _layout.BoxOf(output.Output);
            output.Position = new Point(box.X, box.Y);
            output.Dimensions = new Size(box.Width, box.Height);
        }

        System.Diagnostics.Debug.Assert(
            Arrangement.IsDisjoint(_layout),
            "logical output areas must not overlap when manage_start is sent");
    }

    private Point ClampIntoOutputs(Point point)
    {
        foreach (var output in _outputs)
        {
            var area = new Box(output.Position.X, output.Position.Y, output.Width, output.Height);
            if (area.Contains(point))
            {
                return point;
            }
        }

        var (x, y) = _layout.ClosestPoint(point.X, point.Y);
        return new Point((int)x, (int)y);
    }

    private IEventSource? _watchdog;

    private void ArmUnresponsiveWatchdog()
    {
        if (_options.UnresponsiveTimeoutMs <= 0)
        {
            return;
        }

        _watchdog ??= _loop.AddTimer(OnUnresponsive);
        _watchdog.UpdateTimer(_options.UnresponsiveTimeoutMs);
    }

    private void DisarmUnresponsiveWatchdog() => _watchdog?.UpdateTimer(0);

    private void OnUnresponsive()
    {
        if (_state is not (SequenceState.Manage or SequenceState.Render))
        {
            return;
        }

        UnresponsiveSequences++;
        WindowManagerUnresponsive?.Invoke();

        if (_options.DisconnectUnresponsiveManager && _manager is { IsDestroyed: false } manager)
        {
            manager.PostError(
                (uint)RiverWindowManagerV1.Error.Unresponsive,
                "the window manager did not finish its sequence in time");
            DetachManager();
        }
    }
}
