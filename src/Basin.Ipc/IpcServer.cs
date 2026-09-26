using Basin.Capabilities;
using Basin.Diagnostics;
using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

public sealed class IpcServer : IDisposable
{
    private const int Backlog = 16;

    private readonly ICompositorEventLoop _loop;
    private readonly List<IpcConnection> _connections = [];
    private readonly List<IDisposable> _owned = [];
    private readonly string? _requestedPath;
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
        BasinCounters.Track();
    }

    public BasinServices Services { get; }

    public bool Listens { get; }

    public ISyntheticInput? SyntheticInput { get; set; }

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
        if (Methods.TryInvoke(method, parameters, reply))
        {
            return true;
        }

        reply.Error(IpcErrorCodes.UnknownMethod, $"no method '{method}' on this compositor");
        return false;
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

        LineFront?.Dispose();
        LineFront = null;
        Events.Dispose();
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

    private void Listen()
    {
        var path = _requestedPath ?? DefaultPath(Session.WaylandSocket);
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

                _connections.Add(new IpcConnection(this, client, _loop));
            }
        }
        catch (Exception exception)
        {
            Log.Error($"accepting a control connection failed: {exception}");
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
