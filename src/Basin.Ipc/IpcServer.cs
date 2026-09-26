using System.Diagnostics;
using Basin.Capabilities;
using Basin.Diagnostics;
using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

public sealed unsafe class IpcServer : IDisposable
{
    private const int Backlog = 16;

    private readonly ICompositorEventLoop _loop;
    private readonly List<IpcConnection> _connections = [];
    private readonly List<IDisposable> _owned = [];
    private readonly List<IpcHeldCall> _held = [];
    private readonly List<IpcCallContext> _contexts = [];
    private readonly string? _requestedPath;
    private IIpcInterceptor? _interceptor;
    private IpcApprovalBroker? _approvals;
    private int _depth;
    private IEventSource? _listenSource;
    private int _listenFd = -1;
    private bool _disposed;

    public IpcServer(
        ICompositorEventLoop loop, BasinServices services, IpcSessionInfo session, string? socketPath, bool listen = true)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(session);
        _loop = loop;
        Services = services;
        Session = session;
        _requestedPath = socketPath;
        Listens = listen;
        Processes = new IpcProcessTracker(loop, SessionEnvironment);
        BasinCounters.Track();
    }

    public IpcProcessTracker Processes { get; }

    public BasinServices Services { get; }

    public bool Listens { get; }

    public ISyntheticInput? SyntheticInput { get; set; }

    public IIpcInterceptor? Interceptor
    {
        get => _interceptor;
        set
        {
            ThrowIfStartedOrDisposed();
            _interceptor = value;
        }
    }

    public int HeldCount => _held.Count;

    public IpcApprovalBroker? Approvals
    {
        get => _approvals;
        set
        {
            ThrowIfStartedOrDisposed();
            _approvals = value;
        }
    }

    public void Omit(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        ThrowIfStartedOrDisposed();
        foreach (var name in names)
        {
            Methods.Omit(name);
        }
    }

    public IpcSessionInfo Session { get; }

    public ICompositorEventLoop Loop => _loop;

    public IpcMethodRegistry Methods { get; } = new();

    public IpcEventBus Events { get; } = new();

    public string? Path { get; private set; }

    public bool IsStarted { get; private set; }

    public bool IsDisposed => _disposed;

    public int ConnectionCount => _connections.Count;

    public IpcLineFront? LineFront { get; private set; }

    internal IpcDescribe Describe { get; private set; } = null!;

    public static string? DefaultPath(string? waylandSocket)
    {
        var name = string.IsNullOrEmpty(waylandSocket) ? Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) : System.IO.Path.GetFileName(waylandSocket);
        return IpcProtocol.SocketPath(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), name);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStarted)
        {
            throw new InvalidOperationException("the server starts once");
        }

        SyntheticInput ??= Services.Find<ISyntheticInput>();
        Describe = new IpcDescribe(Services);
        if (SyntheticInput is { } input)
        {
            Describe.PointerPosition = () => input.TryPointerPosition(out var x, out var y) ? (x, y) : null;
        }

        IpcLibrary.Register(this);
        Methods.Freeze();
        IsStarted = true;
        if (Listens)
        {
            Listen();
        }
    }

    public IpcLineFront StartLineFront(int fd = 0)
    {
        ThrowIfNotStarted();
        if (LineFront is not null)
        {
            throw new InvalidOperationException("the line front is already running");
        }

        LineFront = new IpcLineFront(this, fd);
        return LineFront;
    }

    public void Adopt(int fd)
    {
        ThrowIfNotStarted();
        ArgumentOutOfRangeException.ThrowIfNegative(fd);
        _connections.Add(new IpcConnection(this, fd, _loop));
    }

    public void Own(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _owned.Add(resource);
    }

    public void Disown(IDisposable resource) => _owned.Remove(resource);

    public bool Invoke(
        string method, ReadOnlySpan<byte> id, ReadOnlySpan<byte> parameters, IpcReply reply, object? frontState = null)
    {
        ArgumentNullException.ThrowIfNull(reply);
        reply.Begin(id, method);
        reply.FrontState = frontState;
        if (_interceptor is { } interceptor && Methods.Contains(method))
        {
            Intercept(interceptor, method, parameters, reply);
            return true;
        }

        if (Methods.TryInvoke(method, parameters, reply))
        {
            return true;
        }

        reply.Error(IpcErrorCodes.UnknownMethod, $"no method '{method}' on this compositor");
        return false;
    }

    internal void RunHeld(IpcHeldCall call, IpcPendingReply reply, ReadOnlySpan<byte> parameters)
    {
        reply.Intercepted = this;
        reply.CallStarted = call.Started;
        reply.Rerun = true;
        _ = Methods.TryInvoke(call.Method, parameters, reply);
        if (!reply.IsDeferred)
        {
            reply.Rerun = false;
            _ = reply.Complete();
        }
    }

    internal void ForgetHeld(IpcHeldCall call) => _held.Remove(call);

    internal void After(string method, IpcReply reply, long started)
    {
        if (_interceptor is not { } interceptor)
        {
            return;
        }

        var context = RentContext();
        context.BeginAfter(method, reply.State, reply.Sink is IpcLineFront);
        try
        {
            interceptor.After(method, new IpcCallOutcome(reply.ErrorCode, reply.ErrorMessage), Stopwatch.GetElapsedTime(started), context);
        }
        catch (Exception exception)
        {
            Log.Error($"the interceptor failed after {method}: {exception}");
        }
        finally
        {
            ReturnContext(context);
        }
    }

    private void Intercept(IIpcInterceptor interceptor, string method, ReadOnlySpan<byte> parameters, IpcReply reply)
    {
        var started = Stopwatch.GetTimestamp();
        var context = RentContext();
        IpcDecision decision;
        IpcHeldCall? held;
        try
        {
            fixed (byte* bytes = parameters)
            {
                context.Begin(reply, method, bytes, parameters.Length, started);
                try
                {
                    decision = interceptor.Before(method, parameters, context);
                }
                catch (Exception exception)
                {
                    Log.Error($"the interceptor failed before {method}: {exception}");
                    decision = IpcDecision.Deny("the compositor could not decide on this call");
                }

                held = context.Held;
            }
        }
        finally
        {
            ReturnContext(context);
        }

        switch (decision.Kind)
        {
            case IpcDecisionKind.Allow:
                reply.Intercepted = this;
                reply.CallStarted = started;
                _ = Methods.TryInvoke(method, parameters, reply);
                if (!reply.IsDeferred)
                {
                    reply.Intercepted = null;
                    After(method, reply, started);
                }

                return;

            case IpcDecisionKind.Defer when held is not null:
                var pending = reply.Defer();
                pending.Intercepted = this;
                pending.CallStarted = started;
                _held.Add(held);
                held.Attach(pending);
                return;

            case IpcDecisionKind.Defer:
                reply.Error(IpcErrorCodes.Internal, "the interceptor deferred a call it did not hold");
                After(method, reply, started);
                return;

            default:
                reply.Error(IpcErrorCodes.Refused, decision.Message ?? "the compositor refused the call");
                After(method, reply, started);
                return;
        }
    }

    private IpcCallContext RentContext()
    {
        if (_depth == _contexts.Count)
        {
            _contexts.Add(new IpcCallContext(this));
        }

        return _contexts[_depth++];
    }

    private void ReturnContext(IpcCallContext context)
    {
        context.End();
        _depth--;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenSource?.Remove();
        _listenSource = null;
        if (_listenFd >= 0)
        {
            _ = UnixSocket.Close(_listenFd);
            _listenFd = -1;
        }

        for (var i = _connections.Count - 1; i >= 0; i--)
        {
            _connections[i].Dispose();
        }

        _approvals?.Detach();
        foreach (var held in _held.ToArray())
        {
            if (!held.IsDone)
            {
                held.Deny("the compositor stopped");
            }
        }

        _held.Clear();
        LineFront?.Dispose();
        LineFront = null;
        Events.Dispose();
        Processes.Dispose();
        for (var i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }

        _owned.Clear();
        if (Path is { } path)
        {
            _ = UnixSocket.Unlink(path);
            if (Environment.GetEnvironmentVariable(IpcProtocol.SocketVariable) == path)
            {
                IpcNative.Export(IpcProtocol.SocketVariable, null);
            }

            Path = null;
        }

        BasinCounters.Untrack();
    }

    internal void Forget(IpcConnection connection) => _connections.Remove(connection);

    private Dictionary<string, string> SessionEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Session.WaylandSocket is { Length: > 0 } socket)
        {
            values["WAYLAND_DISPLAY"] = socket;
        }

        if (Session.XwaylandDisplay?.Invoke() is { Length: > 0 } display)
        {
            values["DISPLAY"] = display;
        }

        if (Path is { } path)
        {
            values[IpcProtocol.SocketVariable] = path;
        }

        return values;
    }

    private void Listen()
    {
        var inherited = _requestedPath is null ? Environment.GetEnvironmentVariable(IpcProtocol.PathVariable) : null;
        if (inherited is not null)
        {
            IpcNative.Export(IpcProtocol.PathVariable, null);
            if (!System.IO.Path.IsPathRooted(inherited))
            {
                Log.Warn($"{IpcProtocol.PathVariable} is not absolute; the default path is used");
                inherited = null;
            }
        }

        var path = _requestedPath ?? (string.IsNullOrEmpty(inherited) ? DefaultPath(Session.WaylandSocket) : inherited);
        if (path is not null && Environment.GetEnvironmentVariable(IpcProtocol.PathVariable) == path)
        {
            IpcNative.Export(IpcProtocol.PathVariable, null);
        }

        if (path is null)
        {
            Log.Warn($"XDG_RUNTIME_DIR is not set; the control socket is off");
            return;
        }

        if (!UnixSocket.FitsPath(path))
        {
            Log.Warn($"'{path}' is too long for a Unix socket; the control socket is off");
            return;
        }

        if (File.Exists(path) && !ClearStale(path))
        {
            Log.Warn($"a live compositor already answers on '{path}'; the control socket is off");
            return;
        }

        var fd = UnixSocket.Create();
        if (fd < 0)
        {
            Log.Warn($"socket() failed with errno {UnixSocket.LastError}; the control socket is off");
            return;
        }

        if (UnixSocket.Bind(fd, path) != 0)
        {
            Log.Warn($"bind('{path}') failed with errno {UnixSocket.LastError}; the control socket is off");
            _ = UnixSocket.Close(fd);
            return;
        }

        if (UnixSocket.Chmod(path, Convert.ToUInt32("600", 8)) != 0 || UnixSocket.Listen(fd, Backlog) != 0)
        {
            Log.Warn($"could not secure or listen on '{path}' (errno {UnixSocket.LastError}); the control socket is off");
            _ = UnixSocket.Close(fd);
            _ = UnixSocket.Unlink(path);
            return;
        }

        _listenFd = fd;
        Path = path;
        _listenSource = _loop.AddFd(fd, FdReadiness.Readable, OnAcceptReady);
        IpcNative.Export(IpcProtocol.SocketVariable, path);
        BasinReport.Line($"IPC {path}");
    }

    private static bool ClearStale(string path)
    {
        var probe = UnixSocket.Create();
        if (probe < 0)
        {
            return false;
        }

        var connected = UnixSocket.Connect(probe, path) == 0 || UnixSocket.LastError == UnixSocket.EAgain;
        _ = UnixSocket.Close(probe);
        if (connected)
        {
            return false;
        }

        _ = UnixSocket.Unlink(path);
        return true;
    }

    private void OnAcceptReady(int fd, FdReadiness readiness)
    {
        try
        {
            while (true)
            {
                var client = UnixSocket.Accept(_listenFd);
                if (client < 0)
                {
                    var error = UnixSocket.LastError;
                    if (error != UnixSocket.EAgain && error != UnixSocket.EIntr)
                    {
                        Log.Debug($"accept failed with errno {error}");
                    }

                    if (error == UnixSocket.EIntr)
                    {
                        continue;
                    }

                    return;
                }

                if (!UnixSocket.TryPeerCredentials(client, out var pid, out var uid) || uid != UnixSocket.EffectiveUid())
                {
                    Log.Info($"refused a control connection from uid {uid} (pid {pid})");
                    _ = UnixSocket.Close(client);
                    continue;
                }

                var connection = new IpcConnection(this, client, _loop);
                connection.State.Pid = pid;
                _connections.Add(connection);
            }
        }
        catch (Exception exception)
        {
            Log.Error($"accepting a control connection failed: {exception}");
        }
    }

    private void ThrowIfStartedOrDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStarted)
        {
            throw new InvalidOperationException("set this before the server starts");
        }
    }

    private void ThrowIfNotStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsStarted)
        {
            throw new InvalidOperationException("start the server first");
        }
    }
}
