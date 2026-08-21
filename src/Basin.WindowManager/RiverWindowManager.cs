using System.Diagnostics;
using Basin.WindowManager.Protocol;
using Wayland;

namespace Basin.WindowManager;

public sealed class RiverWindowManager : IDisposable
{
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

    public RiverWindowManager(string? socket = null)
        : this(WlDisplay.Connect(socket!), null, uint.MaxValue, uint.MaxValue)
    {
    }

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

    public uint Version { get; }

    public WlCompositor? Compositor { get; }

    public WlShm? Shm { get; }

    public WlRegistry Registry => _registry;

    public WlDisplay Display => _display;

    public WmBindings Bindings { get; }

    public WmLayerShell? LayerShell { get; }

    public IWmEventLoop Loop => _loop;

    public bool SessionIsLocked => _sessionLocked;

    public WmSequence Sequence => _sequence;

    public WmLatency ManageLatency { get; } = new();

    public WmLatency RenderLatency { get; } = new();

    public event Action<ManageContext>? Manage;

    public event Action<RenderContext>? Render;

    public event Action? SessionLocked;

    public event Action? SessionUnlocked;

    public event Action? Unavailable;

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

    public WmShellSurface CreateShellSurface(WlSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        WmThreadAffinity.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new WmShellSurface(this, _wm.GetShellSurface(surface), surface);
    }

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

    public void RequestManage()
    {
        WmThreadAffinity.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_finished && !_unavailable)
        {
            _wm.ManageDirty();
        }
    }

    public void ExitSession()
    {
        WmThreadAffinity.Assert();
        RequireVersion(4, "exit_session");
        _wm.ExitSession();
        _display.Flush();
    }

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
