using System.Diagnostics;
using Basin.WindowManager.Protocol;
using Wayland;

namespace Basin.WindowManager;

/// <summary>
/// A window-manager connection to a compositor that serves <c>river_window_manager_v1</c>, and the loop that drives it. Every member must run on the thread that created it.
/// </summary>
public sealed class RiverWindowManager : IDisposable
{
    /// <summary>
    /// The highest <c>river_window_manager_v1</c> version that this library binds.
    /// </summary>
    public const uint MaxVersion = 5;

    private readonly WmEventLoop _loop = new();
    private readonly Dictionary<RiverWindowV1, WmWindow> _windowsByProxy = [];
    private readonly Dictionary<RiverOutputV1, WmOutput> _outputsByProxy = [];
    private readonly Dictionary<RiverSeatV1, WmSeat> _seatsByProxy = [];
    private readonly Dictionary<RiverShellSurfaceV1, WmShellSurface> _shellSurfacesByProxy = [];
    private readonly List<WmWindow> _windows = [];
    private readonly List<WmOutput> _outputs = [];
    private readonly List<WmSeat> _seats = [];
    private readonly List<WmWindow> _newWindows = [];
    private readonly List<WmWindow> _closedWindows = [];
    private readonly List<WmOutput> _removedOutputs = [];
    private readonly List<WmSeat> _removedSeats = [];
    private readonly List<WmShellSurface> _syncPending = [];

    private readonly WlDisplay _display;
    private readonly WlRegistry _registry;
    private readonly RiverWindowManagerV1 _wm;
    private readonly ManageContext _manageContext;
    private readonly RenderContext _renderContext;

    private WmSequence _sequence;
    private bool _unavailable;
    private bool _finished;
    private bool _stopping;
    private bool _disposed;
    private bool _sessionLocked;
    private bool _sessionLockChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="RiverWindowManager"/> class. It connects to a compositor socket and binds the window-management globals with a blocking roundtrip.
    /// </summary>
    /// <param name="socket">The socket name, or <see langword="null"/> for the one in <c>WAYLAND_DISPLAY</c>.</param>
    /// <exception cref="InvalidOperationException">The compositor does not serve <c>river_window_manager_v1</c>.</exception>
    public RiverWindowManager(string? socket = null)
        : this(WlDisplay.Connect(socket!), null, uint.MaxValue, uint.MaxValue)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RiverWindowManager"/> class over a connected socket fd.
    /// </summary>
    /// <param name="fd">A connected Wayland socket. The connection takes ownership of it.</param>
    /// <param name="pumpServer">For a compositor on the same thread: a step of its dispatch, which the constructor calls in place of a blocking roundtrip. <see langword="null"/> uses a roundtrip.</param>
    /// <exception cref="InvalidOperationException">The compositor does not serve <c>river_window_manager_v1</c>.</exception>
    public RiverWindowManager(int fd, Action? pumpServer = null)
        : this(WlDisplay.ConnectToFd(fd), pumpServer, uint.MaxValue, uint.MaxValue)
    {
    }

    internal RiverWindowManager(int fd, Action? pumpServer, uint managementCap, uint bindingsCap)
        : this(WlDisplay.ConnectToFd(fd), pumpServer, managementCap, bindingsCap)
    {
    }

    private RiverWindowManager(WlDisplay display, Action? pumpServer, uint managementCap, uint bindingsCap)
    {
        WmThreadAffinity.Claim();
        _display = display;

        RiverWindowManagerV1? wm = null;
        RiverXkbBindingsV1? xkbBindings = null;
        RiverLayerShellV1? layerShell = null;
        var backgroundEffects = new WmBackgroundEffects();
        using var settle = display.CreateQueue("basin-background-effect");
        WlCompositor? compositor = null;
        WlShm? shm = null;
        _registry = _display.GetRegistry();
        _registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "river_window_manager_v1":
                    wm = _registry.Bind<RiverWindowManagerV1>(e.Name, Math.Min(e.Version, Math.Min(MaxVersion, managementCap)));
                    break;
                case "river_xkb_bindings_v1":
                    xkbBindings = _registry.Bind<RiverXkbBindingsV1>(
                        e.Name,
                        Math.Min(e.Version, Math.Min((uint)RiverXkbBindingsV1.Interface.Version, bindingsCap)));
                    break;
                case "river_layer_shell_v1":
                    layerShell = _registry.Bind<RiverLayerShellV1>(e.Name, 1);
                    break;
                case "ext_background_effect_manager_v1":
                    backgroundEffects.Bind(_registry.Bind<ExtBackgroundEffectManagerV1>(e.Name, 1), settle);
                    break;
                case "wl_compositor":
                    compositor = _registry.Bind<WlCompositor>(e.Name, Math.Min(e.Version, 6));
                    break;
                case "wl_shm":
                    shm = _registry.Bind<WlShm>(e.Name, 1);
                    break;
            }
        };
        if (pumpServer is null)
        {
            _display.Roundtrip();
        }
        else
        {
            for (var round = 0; round < 64 && wm is null; round++)
            {
                _display.Flush();
                pumpServer();
                DrainSocket();
            }
        }

        _wm = wm ?? throw new InvalidOperationException(
            "the compositor does not implement river_window_manager_v1");
        Version = _wm.Version;
        Compositor = compositor;
        Shm = shm;

        _renderContext = new RenderContext(this);
        _manageContext = new ManageContext(this, _renderContext);
        Bindings = new WmBindings(this, xkbBindings);
        LayerShell = layerShell is null ? null : new WmLayerShell(this, layerShell);
        backgroundEffects.Settle(settle, wait: pumpServer is null);
        backgroundEffects.Attach(compositor);
        BackgroundEffects = backgroundEffects;

        _wm.Unavailable += (_, _) => OnUnavailable();
        _wm.Finished += (_, _) => _finished = true;
        _wm.ManageStart += (_, _) => OnManageStart();
        _wm.RenderStart += (_, _) => OnRenderStart();
        _wm.SessionLocked += (_, _) => SetSessionLocked(true);
        _wm.SessionUnlocked += (_, _) => SetSessionLocked(false);
        _wm.Window += (_, e) => OnWindowCreated(e.Id);
        _wm.Output += (_, e) => OnOutputCreated(e.Id);
        _wm.Seat += (_, e) => OnSeatCreated(e.Id);
    }

    /// <summary>
    /// Gets the bound version of <c>river_window_manager_v1</c>. Requests that need a higher version throw <see cref="NotSupportedException"/>.
    /// </summary>
    public uint Version { get; }

    /// <summary>
    /// Gets the bound <c>wl_compositor</c>, or <see langword="null"/> when the compositor does not serve it.
    /// </summary>
    public WlCompositor? Compositor { get; }

    /// <summary>
    /// Gets the bound <c>wl_shm</c>, or <see langword="null"/> when the compositor does not serve it.
    /// </summary>
    public WlShm? Shm { get; }

    /// <summary>
    /// Gets the connection's registry, for a manager that binds more globals itself.
    /// </summary>
    public WlRegistry Registry => _registry;

    /// <summary>
    /// Gets the Wayland connection. The manager owns it and disposes it in <see cref="Dispose"/>.
    /// </summary>
    public WlDisplay Display => _display;

    /// <summary>
    /// Gets the key and pointer bindings. It is always present, and <see cref="WmBindings.IsSupported"/> says whether key bindings work.
    /// </summary>
    public WmBindings Bindings { get; }

    /// <summary>
    /// Gets the river layer shell, or <see langword="null"/> when the compositor does not serve it.
    /// </summary>
    public WmLayerShell? LayerShell { get; }

    /// <summary>
    /// Gets the client for background blur. It is always present, and its <see cref="WmBackgroundEffects.Bound"/> property says whether the compositor serves the global.
    /// </summary>
    public WmBackgroundEffects BackgroundEffects { get; }

    /// <summary>
    /// Gets the loop that <see cref="Run"/> drives, for the manager's own fds, timers and idle work.
    /// </summary>
    public IWmEventLoop Loop => _loop;

    /// <summary>
    /// Gets a value indicating whether the session is locked, as of the last lock event that arrived.
    /// </summary>
    public bool SessionIsLocked => _sessionLocked;

    /// <summary>
    /// Gets the sequence that is in progress now.
    /// </summary>
    public WmSequence Sequence => _sequence;

    /// <summary>
    /// Gets the time that each manage callback took, which the compositor waits for.
    /// </summary>
    public WmLatency ManageLatency { get; } = new();

    /// <summary>
    /// Gets the time that each render callback took, which the compositor waits for.
    /// </summary>
    public WmLatency RenderLatency { get; } = new();

    /// <summary>
    /// Occurs for each manage sequence, after window, output and seat state is applied and their request events fired. The library finishes the sequence when the handler returns or throws.
    /// </summary>
    public event Action<ManageContext>? Manage;

    /// <summary>
    /// Occurs for each render sequence, after window state is applied. The library finishes the sequence when the handler returns or throws.
    /// </summary>
    public event Action<RenderContext>? Render;

    /// <summary>
    /// Occurs at the start of a manage sequence when the session became locked since the previous one.
    /// </summary>
    public event Action? SessionLocked;

    /// <summary>
    /// Occurs at the start of a manage sequence when the session became unlocked since the previous one.
    /// </summary>
    public event Action? SessionUnlocked;

    /// <summary>
    /// Occurs when the compositor refuses window management, for example because another manager runs. <see cref="Run"/> then returns.
    /// </summary>
    public event Action? Unavailable;

    /// <summary>
    /// Dispatches the connection and the loop until the compositor finishes, refuses window management or the connection fails.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The manager is disposed.</exception>
    public void Run()
    {
        WmThreadAffinity.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);

        var source = _loop.AddFd(_display.Fd, WmFdReadiness.Readable, (_, events) =>
        {
            if ((events & (WmFdReadiness.Hangup | WmFdReadiness.Error)) != 0)
            {
                _finished = true;
                return;
            }

            try
            {
                _display.Dispatch();
            }
            catch (WaylandException)
            {
                _finished = true;
            }
        });

        try
        {
            while (!_finished && !_unavailable)
            {
                _loop.DrainIdle();
                if (_finished || _unavailable)
                {
                    break;
                }

                try
                {
                    _display.DispatchPending();
                    if (_finished || _unavailable)
                    {
                        break;
                    }

                    _display.Flush();
                }
                catch (WaylandException)
                {
                    break;
                }

                _loop.Dispatch(-1);
            }
        }
        finally
        {
            source.Remove();
        }
    }

    /// <summary>
    /// Gives a surface the shell-surface role, for a bar, a menu or other chrome that the manager draws.
    /// </summary>
    /// <param name="surface">A surface with no role and no buffer attached or committed yet.</param>
    /// <returns>The shell surface, which the caller owns and disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The manager is disposed.</exception>
    public WmShellSurface CreateShellSurface(WlSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        WmThreadAffinity.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new WmShellSurface(this, _wm.GetShellSurface(surface), surface);
    }

    /// <summary>
    /// Reads what the socket holds now and dispatches it, without blocking. It is for a manager that shares a thread with its compositor and cannot call <see cref="Run"/>.
    /// </summary>
    public void DispatchPending()
    {
        WmThreadAffinity.Assert();
        DrainSocket();
    }

    private void DrainSocket()
    {
        while (!_finished && IsReadable(_display.Fd))
        {
            try
            {
                _display.Dispatch();
            }
            catch (WaylandException)
            {
                _finished = true;
                return;
            }
        }

        try
        {
            _display.DispatchPending();
        }
        catch (WaylandException)
        {
            _finished = true;
        }
    }

    private static unsafe bool IsReadable(int fd)
    {
        var descriptor = new PollFd { Fd = fd, Events = PollIn };
        return Poll(&descriptor, 1, 0) > 0 && (descriptor.Revents & PollIn) != 0;
    }

    private const short PollIn = 0x001;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern unsafe int Poll(PollFd* fds, nuint count, int timeout);

    /// <summary>
    /// Asks the compositor for a manage sequence, for a change it cannot see. It does nothing after the compositor finished. Do not use it for a repaint.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The manager is disposed.</exception>
    public void RequestManage()
    {
        WmThreadAffinity.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_finished && !_unavailable)
        {
            _wm.ManageDirty();
        }
    }

    /// <summary>
    /// Asks the compositor to end the session and exit, which disconnects every client. Use it only when the user asks to log out.
    /// </summary>
    /// <exception cref="NotSupportedException">The bound protocol version is lower than 4.</exception>
    public void ExitSession()
    {
        WmThreadAffinity.Assert();
        RequireVersion(4, "exit_session");
        _wm.ExitSession();
        _display.Flush();
    }

    /// <summary>
    /// Tells the compositor that the manager wants no more events. <see cref="Run"/> returns when the compositor confirms. A second call does nothing.
    /// </summary>
    public void Stop()
    {
        WmThreadAffinity.Assert();
        if (_stopping || _finished || _unavailable || _disposed)
        {
            return;
        }

        _stopping = true;
        _wm.Stop();
        _display.Flush();
    }

    /// <summary>
    /// Destroys every protocol object and closes the connection. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var window in _windows)
        {
            window.DestroyProxy();
        }

        foreach (var output in _outputs)
        {
            output.DestroyProxy();
        }

        foreach (var seat in _seats)
        {
            seat.DestroyProxy();
        }

        foreach (var output in _removedOutputs)
        {
            output.DestroyProxy();
        }

        foreach (var seat in _removedSeats)
        {
            seat.DestroyProxy();
        }

        _windows.Clear();
        _outputs.Clear();
        _seats.Clear();
        _windowsByProxy.Clear();
        _outputsByProxy.Clear();
        _seatsByProxy.Clear();
        _shellSurfacesByProxy.Clear();
        _newWindows.Clear();
        _closedWindows.Clear();
        _removedOutputs.Clear();
        _removedSeats.Clear();
        _syncPending.Clear();

        Bindings.Dispose();
        LayerShell?.Dispose();
        BackgroundEffects.Dispose();
        _wm.Destroy();
        _registry.Dispose();
        _loop.Clear();
        _display.Dispose();
    }

    internal IReadOnlyList<WmWindow> Windows => _windows;

    internal IReadOnlyList<WmOutput> Outputs => _outputs;

    internal IReadOnlyList<WmSeat> Seats => _seats;

    internal IReadOnlyList<WmWindow> NewWindows => _newWindows;

    internal IReadOnlyList<WmWindow> ClosedWindows => _closedWindows;

    internal RiverWindowManagerV1 Proxy => _wm;

    internal void EnsureManage(string request)
    {
        WmThreadAffinity.Assert();
        if (_sequence != WmSequence.Manage)
        {
            throw new InvalidOperationException(
                $"'{request}' modifies window management state and is only legal inside a manage sequence; current sequence is {_sequence}");
        }
    }

    internal void EnsureRender(string request)
    {
        WmThreadAffinity.Assert();
        if (_sequence == WmSequence.None)
        {
            throw new InvalidOperationException(
                $"'{request}' modifies rendering state and is only legal inside a manage or render sequence");
        }
    }

    internal void RequireVersion(uint since, string request)
    {
        if (Version < since)
        {
            throw new NotSupportedException(
                $"'{request}' requires river_window_management_v1 version {since}; the compositor bound version {Version}");
        }
    }

    internal WmWindow? Resolve(RiverWindowV1? proxy) =>
        proxy is not null && _windowsByProxy.TryGetValue(proxy, out var window) ? window : null;

    internal WmOutput? Resolve(RiverOutputV1? proxy) =>
        proxy is not null && _outputsByProxy.TryGetValue(proxy, out var output) ? output : null;

    internal WmSeat? Resolve(RiverSeatV1? proxy) =>
        proxy is not null && _seatsByProxy.TryGetValue(proxy, out var seat) ? seat : null;

    internal WmShellSurface? Resolve(RiverShellSurfaceV1? proxy) =>
        proxy is not null && _shellSurfacesByProxy.TryGetValue(proxy, out var surface) ? surface : null;

    internal void RegisterShellSurface(RiverShellSurfaceV1 proxy, WmShellSurface surface) =>
        _shellSurfacesByProxy[proxy] = surface;

    internal void UnregisterShellSurface(RiverShellSurfaceV1 proxy) =>
        _shellSurfacesByProxy.Remove(proxy);

    internal void TrackSyncNextCommit(WmShellSurface surface)
    {
        if (!_syncPending.Contains(surface))
        {
            _syncPending.Add(surface);
        }
    }

    internal void ClearSyncNextCommit(WmShellSurface surface) => _syncPending.Remove(surface);

    private void OnUnavailable()
    {
        _unavailable = true;
        Unavailable?.Invoke();
    }

    private void SetSessionLocked(bool locked)
    {
        if (_sessionLocked == locked)
        {
            return;
        }

        _sessionLocked = locked;
        _sessionLockChanged = true;
    }

    private void OnWindowCreated(RiverWindowV1 proxy)
    {
        var window = new WmWindow(this, proxy);
        _windowsByProxy[proxy] = window;
        _windows.Add(window);
        _newWindows.Add(window);
    }

    private void OnOutputCreated(RiverOutputV1 proxy)
    {
        var output = new WmOutput(this, proxy);
        _outputsByProxy[proxy] = output;
        _outputs.Add(output);
    }

    private void OnSeatCreated(RiverSeatV1 proxy)
    {
        var seat = new WmSeat(this, proxy);
        _seatsByProxy[proxy] = seat;
        _seats.Add(seat);
    }

    internal void OnWindowClosed(WmWindow window)
    {
        _closedWindows.Add(window);
        _windows.Remove(window);
    }

    internal void OnOutputRemoved(WmOutput output)
    {
        _outputs.Remove(output);
        _removedOutputs.Add(output);
    }

    internal void OnSeatRemoved(WmSeat seat)
    {
        _seats.Remove(seat);
        _removedSeats.Add(seat);
    }

    private void OnManageStart()
    {
        ApplyPending();

        if (_sessionLockChanged)
        {
            _sessionLockChanged = false;
            if (_sessionLocked)
            {
                SessionLocked?.Invoke();
            }
            else
            {
                SessionUnlocked?.Invoke();
            }
        }

        FirePendingNotifications(skipNewWindows: true);

        _sequence = WmSequence.Manage;
        _manageContext.Revive();
        _renderContext.Revive();
        var started = Stopwatch.GetTimestamp();
        try
        {
            Manage?.Invoke(_manageContext);
        }
        finally
        {
            ManageLatency.Record(Stopwatch.GetTimestamp() - started);
            _manageContext.Kill();
            _renderContext.Kill();
            _sequence = WmSequence.None;
            _wm.ManageFinish();
            ReleaseClosedWindows();
            ReleaseRemovedGlobals();
            _newWindows.Clear();
            if (_deferredNotifications)
            {
                _deferredNotifications = false;
                RequestManage();
            }
        }
    }

    private void OnRenderStart()
    {
        ApplyPending();
        FirePendingNotifications();

        _sequence = WmSequence.Render;
        _renderContext.Revive();
        var started = Stopwatch.GetTimestamp();
        try
        {
            Render?.Invoke(_renderContext);
        }
        finally
        {
            RenderLatency.Record(Stopwatch.GetTimestamp() - started);
            _renderContext.Kill();
            _sequence = WmSequence.None;

            _wm.RenderFinish();
            AssertSyncedCommits();
        }
    }

    private void ApplyPending()
    {
        foreach (var window in _windows)
        {
            window.ApplyPending();
        }

        foreach (var output in _outputs)
        {
            output.ApplyPending();
        }

        foreach (var seat in _seats)
        {
            seat.ApplyPending();
        }
    }

    private bool _deferredNotifications;

    private void FirePendingNotifications(bool skipNewWindows = false)
    {
        foreach (var window in _windows)
        {
            if (skipNewWindows && _newWindows.Contains(window))
            {
                _deferredNotifications |= window.HasPendingNotifications;
                continue;
            }

            window.FirePending();
        }

        foreach (var output in _outputs)
        {
            output.FirePending();
        }

        foreach (var seat in _seats)
        {
            seat.FirePending();
        }

        foreach (var output in _removedOutputs)
        {
            output.FirePending();
        }

        foreach (var seat in _removedSeats)
        {
            seat.FirePending();
        }
    }

    private void ReleaseClosedWindows()
    {
        if (_closedWindows.Count == 0)
        {
            return;
        }

        foreach (var window in _closedWindows)
        {
            _windowsByProxy.Remove(window.ProxyForRemoval);
            window.DestroyProxy();
        }

        _closedWindows.Clear();
    }

    private void ReleaseRemovedGlobals()
    {
        if (_removedOutputs.Count > 0)
        {
            foreach (var output in _removedOutputs)
            {
                _outputsByProxy.Remove(output.Proxy);
                output.DestroyProxy();
            }

            _removedOutputs.Clear();
        }

        if (_removedSeats.Count > 0)
        {
            foreach (var seat in _removedSeats)
            {
                _seatsByProxy.Remove(seat.Proxy);
                seat.DestroyProxy();
            }

            _removedSeats.Clear();
        }
    }

    private void AssertSyncedCommits()
    {
        if (_syncPending.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", _syncPending.Select(static s => s.ToString()));
        _syncPending.Clear();
        throw new InvalidOperationException(
            $"SyncNextCommit was called without a following surface commit before render_finish: {names}. " +
            "The compositor treats this as a protocol error and would disconnect the window manager.");
    }
}
