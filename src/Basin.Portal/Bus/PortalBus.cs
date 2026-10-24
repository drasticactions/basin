using Basin.Diagnostics;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// The D-Bus connection of the portal backend. It owns the backend name, the request and session objects, and the installed modules, and runs every method call on the compositor loop.
/// </summary>
public sealed class PortalBus : IDisposable
{
    /// <summary>
    /// The bus name that the backend requests when the caller names none.
    /// </summary>
    public const string DefaultBusName = "org.freedesktop.impl.portal.desktop.basin";

    /// <summary>
    /// The well-known name of the <c>xdg-desktop-portal</c> frontend. Only its owner can call the backend.
    /// </summary>
    public const string FrontendName = "org.freedesktop.portal.Desktop";

    /// <summary>
    /// The object path of the backend root. Request and session objects are below it.
    /// </summary>
    public const string RootPath = "/org/freedesktop/portal/desktop";

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly CompositorSynchronizationContext _context;
    private readonly Dictionary<string, PortalRequest> _requests = [];
    private readonly Dictionary<string, PortalSession> _sessions = [];
    private readonly List<PortalModule> _modules = [];
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _ownedByModules;
    private DBusConnection? _connection;
    private NameOwnerWatcher? _watcher;
    private volatile string? _frontendUniqueName;
    private bool _starting;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PortalBus"/> class. The constructor does not connect. <see cref="Start"/> or the first installed module connects.
    /// </summary>
    /// <param name="loop">The compositor loop that runs every method call.</param>
    /// <param name="address">The bus address, or <see langword="null"/> for the session bus.</param>
    /// <param name="busName">The name to request, or <see langword="null"/> for <see cref="DefaultBusName"/>.</param>
    /// <param name="ownedByModules"><see langword="true"/> to dispose the bus when the last module detaches.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="address"/> is <see langword="null"/> and the process has no session bus.</exception>
    public PortalBus(ICompositorEventLoop loop, string? address = null, string? busName = null, bool ownedByModules = false)
    {
        ArgumentNullException.ThrowIfNull(loop);
        Loop = loop;
        Address = address ?? DBusAddress.Session ?? throw new InvalidOperationException(
            "This session has no D-Bus session bus. Set DBUS_SESSION_BUS_ADDRESS, or pass an address.");
        BusName = busName ?? DefaultBusName;
        _ownedByModules = ownedByModules;
        _context = new CompositorSynchronizationContext(loop);
        Handler = new PortalHandler(this);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the compositor loop that method calls and continuations run on.
    /// </summary>
    public ICompositorEventLoop Loop { get; }

    /// <summary>
    /// Gets the D-Bus address that the bus connects to.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets the name that the bus requests on connect.
    /// </summary>
    public string BusName { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a caller other than the frontend can call the backend. Set it only for tests.
    /// </summary>
    public bool AcceptAnySender { get; set; }

    /// <summary>
    /// Gets a value indicating whether the bus got <see cref="BusName"/>. A second backend on the same bus does not get it and answers no calls.
    /// </summary>
    public bool OwnsName { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the connection is up and <see cref="Started"/> completed.
    /// </summary>
    public bool IsConnected => _connection is not null && _started.Task.IsCompletedSuccessfully;

    /// <summary>
    /// Gets a task that completes on the loop when the connection is up. It faults when the bus is unreachable, and it is canceled by <see cref="Dispose"/>.
    /// </summary>
    public Task Started => _started.Task;

    /// <summary>
    /// Gets the unique name of the frontend that the bus accepts calls from. It is <see langword="null"/> while no frontend is known.
    /// </summary>
    public string? FrontendUniqueName => _frontendUniqueName;

    /// <summary>
    /// Gets the open sessions, keyed by object path.
    /// </summary>
    public IReadOnlyDictionary<string, PortalSession> Sessions => _sessions;

    /// <summary>
    /// Gets the modules that are installed on this bus.
    /// </summary>
    public IReadOnlyList<PortalModule> Modules => _modules;

    /// <summary>
    /// Occurs on the loop when the frontend leaves the bus, after the bus closes every session and request.
    /// </summary>
    public event Action? FrontendLost;

    internal DBusConnection? Connection => _connection;

    internal PortalHandler Handler { get; }

    internal CompositorSynchronizationContext Context => _context;

    internal PortalCall Current { get; set; }

    /// <summary>
    /// Gets a value indicating whether the process environment names a D-Bus session bus.
    /// </summary>
    public static bool HasSessionBus => DBusAddress.Session is not null;

    /// <summary>
    /// Starts to connect in the background. A second call, or a call after <see cref="Dispose"/>, does nothing. <see cref="Started"/> reports the result.
    /// </summary>
    public void Start()
    {
        _thread.Assert();
        if (_starting || _disposed)
        {
            return;
        }

        _starting = true;
        _ = ConnectAsync();
    }

    /// <summary>
    /// Finds an installed module by type.
    /// </summary>
    /// <typeparam name="T">The module type.</typeparam>
    /// <returns>The first installed module of type <typeparamref name="T"/>, or <see langword="null"/> when none is installed.</returns>
    public T? Module<T>()
        where T : PortalModule
    {
        foreach (var module in _modules)
        {
            if (module is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds an open session by object path.
    /// </summary>
    /// <param name="path">The session object path.</param>
    /// <returns>The session, or <see langword="null"/> when no open session has that path.</returns>
    public PortalSession? SessionAt(string path) => _sessions.GetValueOrDefault(path);

    /// <summary>
    /// Registers a request object, so that a <c>Close</c> call from the frontend cancels it. The caller must give it back to <see cref="Release"/>.
    /// </summary>
    /// <param name="handle">The request object path that the frontend gave.</param>
    /// <returns>The request whose token the frontend can cancel.</returns>
    public PortalRequest Track(ObjectPath handle)
    {
        _thread.Assert();
        var request = new PortalRequest(handle);
        _requests[handle.ToString()] = request;
        return request;
    }

    /// <summary>
    /// Removes a request and disposes it. A request that is not registered stays as it is.
    /// </summary>
    /// <param name="request">The request from <see cref="Track"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public void Release(PortalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _thread.Assert();
        if (_requests.Remove(request.Handle.ToString()))
        {
            request.Dispose();
        }
    }

    /// <summary>
    /// Adds a session, so that the frontend can close it by path. A different session at the same path is closed with no <c>Closed</c> signal.
    /// </summary>
    /// <param name="session">The session to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public void Register(PortalSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _thread.Assert();
        var path = session.Handle.ToString();
        if (_sessions.TryGetValue(path, out var existing) && !ReferenceEquals(existing, session))
        {
            existing.Close(emit: false);
        }

        _sessions[path] = session;
        Log.Info($"session {session.Id} for {session.AppId} registered ({session.GetType().Name})");
    }

    internal void Forget(PortalSession session)
    {
        var path = session.Handle.ToString();
        if (_sessions.TryGetValue(path, out var existing) && ReferenceEquals(existing, session))
        {
            _sessions.Remove(path);
        }
    }

    internal bool CloseRequest(string path)
    {
        if (!_requests.TryGetValue(path, out var request))
        {
            return false;
        }

        request.Close();
        return true;
    }

    internal bool CloseSession(string path)
    {
        if (!_sessions.TryGetValue(path, out var session))
        {
            return false;
        }

        session.Close(emit: true);
        return true;
    }

    internal bool AcceptSender(string? sender)
    {
        if (AcceptAnySender || string.IsNullOrEmpty(sender))
        {
            return true;
        }

        var frontend = _frontendUniqueName;
        if (frontend is null)
        {
            _frontendUniqueName = sender;
            Log.Info($"frontend: {sender} adopted from its first call");
            return true;
        }

        return string.Equals(frontend, sender, StringComparison.Ordinal);
    }

    internal void Attach(PortalModule module)
    {
        _thread.Assert();
        if (!_modules.Contains(module))
        {
            _modules.Add(module);
        }

        Handler.Installed |= module.Interface;
        Start();
    }

    internal void Detach(PortalModule module)
    {
        _thread.Assert();
        if (_modules.Remove(module))
        {
            Handler.Installed &= ~module.Interface;
        }

        if (_modules.Count == 0 && _ownedByModules)
        {
            Dispose();
        }
    }

    /// <summary>
    /// Closes every session and request, releases the bus name, and closes the connection. It must run on the loop thread.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _thread.Assert();
        _disposed = true;
        foreach (var session in _sessions.Values.ToArray())
        {
            session.Close(emit: true);
        }

        _sessions.Clear();
        foreach (var request in _requests.Values)
        {
            request.Close();
            request.Dispose();
        }

        _requests.Clear();
        _watcher?.Dispose();
        _watcher = null;
        if (_connection is { } connection)
        {
            _connection = null;
            if (OwnsName)
            {
                try
                {
                    connection.ReleaseNameAsync(BusName).GetAwaiter().GetResult();
                }
                catch (Exception e) when (e is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
                {
                    Log.Debug(e, $"bus: {BusName} not released");
                }
            }

            connection.Dispose();
        }

        _started.TrySetCanceled();
        _context.Dispose();
        BasinCounters.Untrack();
    }

    private async Task ConnectAsync()
    {
        DBusConnection? connection = null;
        try
        {
            connection = new DBusConnection(new DBusConnectionOptions(Address)
            {
                AutoConnect = false,
                OnException = context =>
                {
                    if (!_disposed)
                    {
                        Log.Warn(context.Exception, $"bus: connection failed");
                    }
                },
            });
            await connection.ConnectAsync().ConfigureAwait(false);
            connection.AddMethodHandler(Handler);
            OwnsName = await connection.TryRequestNameAsync(BusName, RequestNameOptions.None).ConfigureAwait(false);
            if (!OwnsName)
            {
                Log.Warn($"bus: {BusName} is owned by another compositor, this one answers no portal calls");
            }

            var watcher = await connection.WatchNameOwnerAsync(FrontendName).ConfigureAwait(false);
            var owner = watcher.GetCurrentOwner();
            _context.Post(_ =>
            {
                if (_disposed)
                {
                    connection.Dispose();
                    watcher.Dispose();
                    return;
                }

                _connection = connection;
                _watcher = watcher;
                if (owner is not null)
                {
                    _frontendUniqueName = NameOwnerWatcher.GetOwnerBusName(owner);
                    Log.Info($"frontend: {_frontendUniqueName} is the portal frontend");
                }

                Log.Info($"bus: {BusName} {(OwnsName ? "owned" : "not owned")} on {Address}");
                _started.TrySetResult();
            }, null);

            await WatchFrontendAsync(watcher, owner).ConfigureAwait(false);
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException or IOException)
        {
            connection?.Dispose();
            if (!_disposed)
            {
                Log.Warn(error, $"bus: not reachable, no portal is offered");
            }

            _started.TrySetException(error);
        }
    }

    private async Task WatchFrontendAsync(NameOwnerWatcher watcher, string? owner)
    {
        while (!_disposed)
        {
            var token = owner is null ? default : watcher.GetOwnerChangedCancellationToken(owner);
            if (owner is not null)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Log.Debug($"frontend: owner {owner} changed");
                }
            }
            else
            {
                try
                {
                    owner = await watcher.WaitForOwnerAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e) when (e is DBusExceptionBase or ObjectDisposedException or OperationCanceledException)
                {
                    return;
                }

                _context.Post(state => OnFrontendChanged((string?)state), owner);
                continue;
            }

            if (_disposed)
            {
                return;
            }

            var next = watcher.GetCurrentOwner();
            if (next == owner)
            {
                continue;
            }

            owner = next;
            _context.Post(state => OnFrontendChanged((string?)state), owner);
        }
    }

    private void OnFrontendChanged(string? owner)
    {
        if (_disposed)
        {
            return;
        }

        if (owner is null)
        {
            Log.Info($"frontend: {_frontendUniqueName ?? "<unknown>"} left the bus, {_sessions.Count} session(s) closed");
            _frontendUniqueName = null;
            foreach (var session in _sessions.Values.ToArray())
            {
                session.Close(emit: false);
            }

            foreach (var request in _requests.Values.ToArray())
            {
                request.Close();
            }

            FrontendLost?.Invoke();
            return;
        }

        _frontendUniqueName = NameOwnerWatcher.GetOwnerBusName(owner);
        Log.Info($"frontend: {_frontendUniqueName} is the portal frontend");
    }
}
