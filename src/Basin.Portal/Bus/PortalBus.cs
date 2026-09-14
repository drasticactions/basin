using Basin.Diagnostics;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class PortalBus : IDisposable
{
    public const string DefaultBusName = "org.freedesktop.impl.portal.desktop.basin";

    public const string FrontendName = "org.freedesktop.portal.Desktop";

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

    public PortalBus(ICompositorEventLoop loop, string? address = null, string? busName = null, bool ownedByModules = false)
    {
        ArgumentNullException.ThrowIfNull(loop);
        Loop = loop;
        Address = address ?? DBusAddress.Session ?? throw new InvalidOperationException(
            "this session has no D-Bus session bus: DBUS_SESSION_BUS_ADDRESS is unset");
        BusName = busName ?? DefaultBusName;
        _ownedByModules = ownedByModules;
        _context = new CompositorSynchronizationContext(loop);
        Handler = new PortalHandler(this);
        BasinCounters.Track();
    }

    public ICompositorEventLoop Loop { get; }

    public string Address { get; }

    public string BusName { get; }

    public bool AcceptAnySender { get; set; }

    public bool OwnsName { get; private set; }

    public bool IsConnected => _connection is not null && _started.Task.IsCompletedSuccessfully;

    public Task Started => _started.Task;

    public string? FrontendUniqueName => _frontendUniqueName;

    public IReadOnlyDictionary<string, PortalSession> Sessions => _sessions;

    public IReadOnlyList<PortalModule> Modules => _modules;

    public event Action? FrontendLost;

    internal DBusConnection? Connection => _connection;

    internal PortalHandler Handler { get; }

    internal CompositorSynchronizationContext Context => _context;

    internal PortalCall Current { get; set; }

    public static bool HasSessionBus => DBusAddress.Session is not null;

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

    public PortalSession? SessionAt(string path) => _sessions.GetValueOrDefault(path);

    public PortalRequest Track(ObjectPath handle)
    {
        _thread.Assert();
        var request = new PortalRequest(handle);
        _requests[handle.ToString()] = request;
        return request;
    }

    public void Release(PortalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _thread.Assert();
        if (_requests.Remove(request.Handle.ToString()))
        {
            request.Dispose();
        }
    }

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
            Log.Info($"portal frontend adopted from its first call: {sender}");
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
                    Log.Debug($"the bus name was not released: {e.Message}");
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
                        Log.Warn($"portal bus: {context.Exception.Message}");
                    }
                },
            });
            await connection.ConnectAsync().ConfigureAwait(false);
            connection.AddMethodHandler(Handler);
            OwnsName = await connection.TryRequestNameAsync(BusName, RequestNameOptions.None).ConfigureAwait(false);
            if (!OwnsName)
            {
                Log.Warn($"{BusName} is owned by another compositor on this bus; this one answers no portal calls");
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
                    Log.Info($"portal frontend is {_frontendUniqueName}");
                }

                Log.Info($"{BusName} {(OwnsName ? "owned" : "not owned")} on {Address}");
                _started.TrySetResult();
            }, null);

            await WatchFrontendAsync(watcher, owner).ConfigureAwait(false);
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException or IOException)
        {
            connection?.Dispose();
            if (!_disposed)
            {
                Log.Warn($"the portal bus is unreachable, no portal is offered: {error.Message}");
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
            Log.Info($"portal frontend {_frontendUniqueName ?? "<unknown>"} left the bus; closing {_sessions.Count} session(s)");
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
        Log.Info($"portal frontend is {_frontendUniqueName}");
    }
}
