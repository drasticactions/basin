using System.Diagnostics;
using Basin.Capabilities;
using Basin.Diagnostics;
using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

/// <summary>
/// The control socket of a compositor: the listening socket, the connections, the method table and the event bus. Everything runs on the compositor thread.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcServer"/> class. The server binds nothing until <see cref="Start"/>.
    /// </summary>
    /// <param name="loop">The compositor loop that carries the socket and connection descriptors.</param>
    /// <param name="services">The services that the library method groups find their capabilities in.</param>
    /// <param name="session">What the program reports about its session.</param>
    /// <param name="socketPath">The socket path, or <see langword="null"/> for <c>BASIN_IPC_PATH</c> or the default path.</param>
    /// <param name="listen"><see langword="false"/> to build the method table for the line front with no socket.</param>
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
        _ownsProcesses = session.Processes is null;
        Processes = session.Processes ?? new ProcessTracker(loop, SessionEnvironment);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the tracker that <c>process/*</c> uses. It is <see cref="IpcSessionInfo.Processes"/>, or one that the server makes and disposes.
    /// </summary>
    public ProcessTracker Processes { get; }

    private readonly bool _ownsProcesses;

    /// <summary>
    /// Gets the services that the library method groups read at <see cref="Start"/>.
    /// </summary>
    public BasinServices Services { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Start"/> binds a socket.
    /// </summary>
    public bool Listens { get; }

    /// <summary>
    /// Gets or sets the injection target of <c>input/*</c>. When it is <see langword="null"/> at <see cref="Start"/>, the server finds one in <see cref="Services"/>.
    /// </summary>
    public ISyntheticInput? SyntheticInput { get; set; }

    /// <summary>
    /// Gets or sets the interceptor that sees every call to a registered method. With none, a call pays nothing more.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server is started.</exception>
    /// <exception cref="ObjectDisposedException">The server is disposed.</exception>
    public IIpcInterceptor? Interceptor
    {
        get => _interceptor;
        set
        {
            ThrowIfStartedOrDisposed();
            _interceptor = value;
        }
    }

    /// <summary>
    /// Gets the number of held calls that wait for an answer.
    /// </summary>
    public int HeldCount => _held.Count;

    /// <summary>
    /// Gets or sets the approval broker. With one, the server registers the <c>approval</c> group.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server is started.</exception>
    /// <exception cref="ObjectDisposedException">The server is disposed.</exception>
    public IpcApprovalBroker? Approvals
    {
        get => _approvals;
        set
        {
            ThrowIfStartedOrDisposed();
            _approvals = value;
        }
    }

    /// <summary>
    /// Leaves library methods out of the table. An omitted method is not listed, and a call to it answers <c>unknown_method</c>.
    /// </summary>
    /// <param name="names">Method names, or <c>group/*</c> for a whole group.</param>
    /// <exception cref="InvalidOperationException">The server is started.</exception>
    /// <exception cref="ArgumentException">A name is neither a method name nor <c>group/*</c>.</exception>
    public void Omit(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        ThrowIfStartedOrDisposed();
        foreach (var name in names)
        {
            Methods.Omit(name);
        }
    }

    /// <summary>
    /// Gets what the program reported about its session.
    /// </summary>
    public IpcSessionInfo Session { get; }

    /// <summary>
    /// Gets the compositor loop that the server runs on.
    /// </summary>
    public ICompositorEventLoop Loop => _loop;

    /// <summary>
    /// Gets the method table. A consumer registers its methods before <see cref="Start"/>.
    /// </summary>
    public IpcMethodRegistry Methods { get; } = new();

    /// <summary>
    /// Gets the event bus that subscriptions and emitted events go through.
    /// </summary>
    public IpcEventBus Events { get; } = new();

    /// <summary>
    /// Gets the bound socket path, or <see langword="null"/> when no socket is bound.
    /// </summary>
    public string? Path { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Start"/> ran.
    /// </summary>
    public bool IsStarted { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the server is disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Gets the number of open socket connections.
    /// </summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>
    /// Gets the stdin line front, or <see langword="null"/> before <see cref="StartLineFront"/>.
    /// </summary>
    public IpcLineFront? LineFront { get; private set; }

    internal IpcDescribe Describe { get; private set; } = null!;

    /// <summary>
    /// Computes the default socket path, <c>$XDG_RUNTIME_DIR/basin-NAME.sock</c>.
    /// </summary>
    /// <param name="waylandSocket">The Wayland socket. Its file name is the NAME. With none, the process id is the NAME.</param>
    /// <returns>The path, or <see langword="null"/> when <c>XDG_RUNTIME_DIR</c> is not set.</returns>
    public static string? DefaultPath(string? waylandSocket)
    {
        var name = string.IsNullOrEmpty(waylandSocket) ? Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) : System.IO.Path.GetFileName(waylandSocket);
        return IpcProtocol.SocketPath(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), name);
    }

    /// <summary>
    /// Registers the library groups whose capabilities are present, freezes the table and binds the socket when <see cref="Listens"/> is set. A bind failure logs a warning, and the compositor runs on with no socket.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server started before.</exception>
    /// <exception cref="ObjectDisposedException">The server is disposed.</exception>
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

    /// <summary>
    /// Starts the line front, which reads commands from a descriptor and runs them on the same method table.
    /// </summary>
    /// <param name="fd">The descriptor to read, 0 for stdin. The front does not own it. A negative value reads nothing, and lines go through <see cref="IpcLineFront.Execute"/> only.</param>
    /// <returns>The line front, which the server disposes.</returns>
    /// <exception cref="InvalidOperationException">The server is not started, or the line front is already running.</exception>
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

    /// <summary>
    /// Takes a connected socket as a new connection, with no peer credential check.
    /// </summary>
    /// <param name="fd">The connected socket. The server owns it from this call.</param>
    /// <exception cref="InvalidOperationException">The server is not started.</exception>
    public void Adopt(int fd)
    {
        ThrowIfNotStarted();
        ArgumentOutOfRangeException.ThrowIfNegative(fd);
        _connections.Add(new IpcConnection(this, fd, _loop));
    }

    /// <summary>
    /// Gives a resource to the server, which disposes it after the connections and the event bus. Owned resources are disposed in reverse order.
    /// </summary>
    /// <param name="resource">The resource.</param>
    public void Own(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _owned.Add(resource);
    }

    /// <summary>
    /// Takes back a resource from <see cref="Own"/> without disposing it.
    /// </summary>
    /// <param name="resource">The resource.</param>
    public void Disown(IDisposable resource) => _owned.Remove(resource);

    /// <summary>
    /// Runs a method as a call from a front. The interceptor sees the call first when the method is registered.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="id">The raw JSON bytes of the request id, which the reply echoes. It is empty when the call has no id.</param>
    /// <param name="parameters">The raw JSON of the params.</param>
    /// <param name="reply">The reply to reset and fill.</param>
    /// <param name="frontState">A value that the front attaches to the reply, such as a line formatter.</param>
    /// <returns><see langword="false"/> when the method is unknown. The reply then carries <c>unknown_method</c>.</returns>
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

    /// <summary>
    /// Closes the socket and every connection, denies every held call and pending approval, disposes owned resources and unlinks the socket path.
    /// </summary>
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
        if (_ownsProcesses)
        {
            Processes.Dispose();
        }
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

    private Dictionary<string, string?> SessionEnvironment() =>
        ProcessTracker.SessionEnvironment(Session.WaylandSocket, Session.XwaylandDisplay?.Invoke(), Path);

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
