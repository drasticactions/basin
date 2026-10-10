using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Basin.Transport.Waypipe;

[UnsupportedOSPlatform("browser")]
public sealed class WaypipeListener : IWaypipeListener
{
    private const int Backlog = 8;
    private const int RetryMillis = 100;

    private readonly ICompositorEventLoop _loop;
    private readonly WaypipeCompression _compression;
    private readonly WaypipeLimits? _limits;
    private readonly WaypipeChannelOptions? _options;
    private readonly Socket? _socket;
    private readonly int _vsockFd = -1;
    private readonly string? _unixPath;
    private readonly CompositorSynchronizationContext _hop;
    private readonly HashSet<WaypipeChannel> _live = [];
    private readonly List<WaypipeChannel> _ended = [];
    private readonly SendOrPostCallback _disposeEnded;
    private IEventSource? _source;
    private IEventSource? _retry;
    private int _accepted;
    private bool _disposed;

    private WaypipeListener(
        ICompositorEventLoop loop,
        EndPoint local,
        Socket? socket,
        int vsockFd,
        string? unixPath,
        WaypipeCompression compression,
        WaypipeLimits? limits,
        WaypipeChannelOptions? options)
    {
        _loop = loop;
        LocalEndPoint = local;
        _socket = socket;
        _vsockFd = vsockFd;
        _unixPath = unixPath;
        _compression = compression;
        _limits = limits;
        _options = options;
        _hop = new CompositorSynchronizationContext(loop);
        _disposeEnded = _ => DisposeEnded();
        Watch();
    }

    public EndPoint LocalEndPoint { get; }

    public int Live
    {
        get
        {
            lock (_live)
            {
                return _live.Count;
            }
        }
    }

    public int Accepted => _accepted;

    public event Action<WaypipeChannel>? ChannelAccepted;

    public event Action<Exception>? Failed;

    public static WaypipeListener Bind(
        ICompositorEventLoop loop,
        EndPoint endpoint,
        WaypipeCompression compression = WaypipeCompression.Lz4,
        WaypipeLimits? limits = null,
        WaypipeChannelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint is VsockEndPoint && OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "a vsock listener on Windows is a Hyper-V socket; bind it with HyperVWaypipeListener.BindAsync or WaypipeListeners.BindAsync");
        }

        if (!PlatformFacts.HasDescriptors)
        {
            throw new PlatformNotSupportedException(
                "this host binds no listening socket; attach a connected stream with AttachChannel instead");
        }

        if (endpoint is VsockEndPoint vsock)
        {
            if (vsock.Port == 0)
            {
                throw new ArgumentException("a vsock listener needs a port", nameof(endpoint));
            }

            return BindVsock(loop, vsock, compression, limits, options);
        }

        if (endpoint is IPEndPoint ip &&
            (ip.Address.Equals(IPAddress.Any) || ip.Address.Equals(IPAddress.IPv6Any)))
        {
            throw new ArgumentException(
                "a waypipe channel binds an explicit address; loopback, a unix socket, vsock or an ssh-forwarded stream is the posture",
                nameof(endpoint));
        }

        var unix = endpoint as UnixDomainSocketEndPoint;
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, unix is null ? ProtocolType.Tcp : ProtocolType.Unspecified);
        string? path = null;
        try
        {
            socket.Bind(endpoint);
            if (unix is not null)
            {
                var name = unix.ToString();
                if (name.Length > 0 && name[0] != '\0' && name[0] != '@')
                {
                    path = name;
                }
            }

            socket.Listen(Backlog);
            socket.Blocking = false;
            return new WaypipeListener(loop, socket.LocalEndPoint ?? endpoint, socket, -1, path, compression, limits, options);
        }
        catch
        {
            socket.Dispose();
            if (path is not null)
            {
                File.Delete(path);
            }

            throw;
        }
    }

    private static WaypipeListener BindVsock(
        ICompositorEventLoop loop,
        VsockEndPoint endpoint,
        WaypipeCompression compression,
        WaypipeLimits? limits,
        WaypipeChannelOptions? options)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("vsock channels are available on Linux only");
        }

        var fd = VsockNative.OpenListening(endpoint, Backlog);
        try
        {
            var local = VsockNative.LocalEndPoint(fd, endpoint);
            WslHint.Log(local);
            return new WaypipeListener(loop, local, null, fd, null, compression, limits, options);
        }
        catch
        {
            _ = VsockNative.Close(fd);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source?.Remove();
        _source = null;
        _retry?.Remove();
        _retry = null;
        if (_socket is not null)
        {
            _socket.Dispose();
        }
        else if (_vsockFd >= 0 && OperatingSystem.IsLinux())
        {
            _ = VsockNative.Close(_vsockFd);
        }

        if (_unixPath is not null)
        {
            try
            {
                File.Delete(_unixPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        WaypipeChannel[] open;
        lock (_live)
        {
            open = [.. _live];
            _live.Clear();
        }

        foreach (var channel in open)
        {
            channel.Dispose();
        }

        DisposeEnded();
        _hop.Dispose();
    }

    private int ListeningFd => _socket is not null ? (int)_socket.Handle : _vsockFd;

    private void Watch() => _source = _loop.AddFd(ListeningFd, FdReadiness.Readable, OnReadable);

    private void OnReadable(int fd, FdReadiness readiness)
    {
        while (!_disposed && _source is not null)
        {
            if (!TryAccept(out var stream, out var shutdown))
            {
                return;
            }

            Attach(stream!, shutdown!);
        }
    }

    private bool TryAccept(out Stream? stream, out Action? shutdown)
    {
        stream = null;
        shutdown = null;
        while (true)
        {
            if (_socket is not null)
            {
                Socket accepted;
                try
                {
                    accepted = _socket.Accept();
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.TryAgain)
                {
                    return false;
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.Interrupted or SocketError.ConnectionAborted)
                {
                    continue;
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TooManyOpenSockets)
                {
                    Pause(ex.Message);
                    return false;
                }
                catch (SocketException ex)
                {
                    Fail(ex);
                    return false;
                }

                accepted.Blocking = true;
                if (accepted.ProtocolType == ProtocolType.Tcp)
                {
                    accepted.NoDelay = true;
                }

                stream = new NetworkStream(accepted, ownsSocket: true);
                shutdown = () => accepted.Shutdown(SocketShutdown.Both);
                return true;
            }

            if (!OperatingSystem.IsLinux())
            {
                return false;
            }

            var fd = VsockNative.Accept(_vsockFd);
            if (fd >= 0)
            {
                var vsock = new VsockStream(fd);
                stream = vsock;
                shutdown = vsock.Shutdown;
                return true;
            }

            var errno = VsockNative.LastError;
            switch (errno)
            {
                case VsockNative.EAgain:
                    return false;
                case VsockNative.EIntr:
                case VsockNative.EConnAborted:
                    continue;
                case VsockNative.EMFile:
                case VsockNative.ENFile:
                    Pause(System.Runtime.InteropServices.Marshal.GetPInvokeErrorMessage(errno));
                    return false;
                default:
                    Fail(new IOException(
                        $"accept4({LocalEndPoint}) failed: {System.Runtime.InteropServices.Marshal.GetPInvokeErrorMessage(errno)} (errno {errno})"));
                    return false;
            }
        }
    }

    private void Attach(Stream stream, Action shutdown)
    {
        var index = Interlocked.Increment(ref _accepted);
        WaypipeChannel channel;
        try
        {
            channel = WaypipeChannel.AttachAccepted(
                stream,
                shutdown,
                _compression,
                _limits,
                _options,
                accepted =>
                {
                    lock (_live)
                    {
                        _live.Add(accepted);
                    }

                    accepted.Ended += _ => OnEnded(accepted);
                });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stream.Dispose();
            WaypipeLog.Log.Warn($"channel {index} on {LocalEndPoint} could not start: {ex.Message}");
            return;
        }

        WaypipeLog.Log.Debug($"channel {index} accepted on {LocalEndPoint}");
        ChannelAccepted?.Invoke(channel);
    }

    private void OnEnded(WaypipeChannel channel)
    {
        bool owned;
        lock (_live)
        {
            owned = _live.Remove(channel);
            if (owned)
            {
                _ended.Add(channel);
            }
        }

        if (owned)
        {
            _hop.Post(_disposeEnded, null);
        }
    }

    private void DisposeEnded()
    {
        WaypipeChannel[] ended;
        lock (_live)
        {
            if (_ended.Count == 0)
            {
                return;
            }

            ended = [.. _ended];
            _ended.Clear();
        }

        foreach (var channel in ended)
        {
            channel.Dispose();
        }
    }

    private void Pause(string reason)
    {
        WaypipeLog.Log.Warn($"accepting on {LocalEndPoint} paused for {RetryMillis} ms: {reason}");
        _source?.Remove();
        _source = null;
        _retry ??= _loop.AddTimer(Resume);
        _retry.UpdateTimer(RetryMillis);
    }

    private void Resume()
    {
        if (_disposed || _source is not null)
        {
            return;
        }

        Watch();
    }

    private void Fail(Exception error)
    {
        WaypipeLog.Log.Error($"accepting on {LocalEndPoint} failed: {error.Message}");
        _source?.Remove();
        _source = null;
        Failed?.Invoke(error);
    }
}
