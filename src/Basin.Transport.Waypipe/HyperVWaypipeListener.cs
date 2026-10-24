using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The Windows listener on a Hyper-V socket. It accepts on a background task and posts every event to the compositor thread. For the WSL2 VM, it binds again when the VM restarts.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HyperVWaypipeListener : IWaypipeListener
{
    private const int Backlog = 8;
    private const int RetryMillis = 100;
    private const int AnyPortFirst = 49152;
    private const int AnyPortLast = 65535;
    private const int AnyPortAttempts = 32;

    private static readonly TimeSpan[] RebindBackoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

    private readonly HyperVVm _vm;
    private readonly uint _port;
    private readonly Action<Action> _post;
    private readonly WaypipeCompression _compression;
    private readonly WaypipeLimits? _limits;
    private readonly WaypipeChannelOptions? _options;
    private readonly HashSet<WaypipeChannel> _live = [];
    private readonly List<WaypipeChannel> _ended = [];
    private readonly List<WaypipeChannel> _unclaimed = [];
    private Action<WaypipeChannel>? _channelAccepted;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _rebinding = new(1, 1);
    private readonly object _gate = new();
    private Socket? _socket;
    private HyperVEndPoint _local;
    private int _accepted;
    private volatile bool _disposed;
    private volatile bool _vmStopped;

    private HyperVWaypipeListener(
        HyperVVm vm,
        Socket socket,
        HyperVEndPoint local,
        Action<Action> post,
        WaypipeCompression compression,
        WaypipeLimits? limits,
        WaypipeChannelOptions? options)
    {
        _vm = vm;
        _socket = socket;
        _local = local;
        _port = local.VsockPort ?? 0;
        _post = post;
        _compression = compression;
        _limits = limits;
        _options = options;
    }

    /// <summary>
    /// Gets the address that the listener binds now. It changes after a rebind.
    /// </summary>
    public HyperVEndPoint LocalEndPoint
    {
        get
        {
            lock (_gate)
            {
                return _local;
            }
        }
    }

    EndPoint IWaypipeListener.LocalEndPoint => LocalEndPoint;

    /// <summary>
    /// Gets the VM that the listener was asked to bind.
    /// </summary>
    public HyperVVm Vm => _vm;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public int Accepted => _accepted;

    /// <summary>
    /// Occurs on the compositor thread for each new channel. Channels that arrived before the first handler go to that handler when it is added.
    /// </summary>
    public event Action<WaypipeChannel>? ChannelAccepted
    {
        add
        {
            _channelAccepted += value;
            if (value is null || _unclaimed.Count == 0)
            {
                return;
            }

            WaypipeChannel[] waiting = [.. _unclaimed];
            _unclaimed.Clear();
            foreach (var channel in waiting)
            {
                value(channel);
            }
        }

        remove => _channelAccepted -= value;
    }

    /// <summary>
    /// Occurs on the compositor thread after the listener binds the same port on a new WSL2 VM id.
    /// </summary>
    public event Action<HyperVEndPoint>? Rebound;

    /// <summary>
    /// Occurs on the compositor thread when accept or every rebind attempt fails.
    /// </summary>
    public event Action<Exception>? Failed;

    /// <summary>
    /// Resolves the VM id and binds a vsock port on it.
    /// </summary>
    /// <param name="endpoint">The vsock endpoint. It must not name a context id.</param>
    /// <param name="vm">The VM to bind.</param>
    /// <param name="post">The delegate that runs an action on the compositor thread.</param>
    /// <param name="compression">The compression that every channel expects from its peer.</param>
    /// <param name="limits">The limits for each channel, or <see langword="null"/> for the defaults.</param>
    /// <param name="options">The content that each channel accepts, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellation">Cancels the VM id query.</param>
    /// <returns>A task that completes with the bound listener, which the caller owns.</returns>
    /// <exception cref="ArgumentException"><paramref name="endpoint"/> names a context id.</exception>
    /// <exception cref="WslVmException">The WSL2 VM id cannot be resolved.</exception>
    /// <exception cref="IOException">The socket cannot be made, bound or put in the listening state.</exception>
    public static async Task<HyperVWaypipeListener> BindAsync(
        VsockEndPoint endpoint,
        HyperVVm vm,
        Action<Action> post,
        WaypipeCompression compression = WaypipeCompression.Lz4,
        WaypipeLimits? limits = null,
        WaypipeChannelOptions? options = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(post);
        if (endpoint.Cid != VsockEndPoint.AnyCid)
        {
            throw new ArgumentException(
                $"'{endpoint}' names a CID; on Windows a listener takes vsock:PORT and the VM comes from --hyperv-vm",
                nameof(endpoint));
        }

        var id = await ResolveAsync(vm, cancellation).ConfigureAwait(false);
        var local = HyperVEndPoint.ForVsockPort(id, endpoint.Port);
        var socket = Open(local, vm);
        return Start(vm, socket, local, post, compression, limits, options);
    }

    /// <summary>
    /// Binds a random port from 49152 through 65535, with up to 32 attempts. <see cref="HyperVEndPoint.VsockPort"/> of <see cref="LocalEndPoint"/> gives the port.
    /// </summary>
    /// <param name="vm">The VM to bind.</param>
    /// <param name="post">The delegate that runs an action on the compositor thread.</param>
    /// <param name="compression">The compression that every channel expects from its peer.</param>
    /// <param name="limits">The limits for each channel, or <see langword="null"/> for the defaults.</param>
    /// <param name="options">The content that each channel accepts, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellation">Cancels the VM id query.</param>
    /// <returns>A task that completes with the bound listener, which the caller owns.</returns>
    /// <exception cref="WslVmException">The WSL2 VM id cannot be resolved.</exception>
    /// <exception cref="IOException">Every attempt failed, or the socket failed for another reason.</exception>
    public static async Task<HyperVWaypipeListener> BindAnyPortAsync(
        HyperVVm vm,
        Action<Action> post,
        WaypipeCompression compression = WaypipeCompression.Lz4,
        WaypipeLimits? limits = null,
        WaypipeChannelOptions? options = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        var id = await ResolveAsync(vm, cancellation).ConfigureAwait(false);
        for (var attempt = 0; ; attempt++)
        {
            var local = HyperVEndPoint.ForVsockPort(id, (uint)Random.Shared.Next(AnyPortFirst, AnyPortLast + 1));
            try
            {
                var socket = Open(local, vm);
                return Start(vm, socket, local, post, compression, limits, options);
            }
            catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } &&
                attempt + 1 < AnyPortAttempts)
            {
            }
        }
    }

    /// <summary>
    /// Closes the socket and binds the same port again on the current VM id. Live channels stay open.
    /// </summary>
    /// <param name="cancellation">Cancels the wait for another rebind and the VM id query.</param>
    /// <returns>A task that completes when the rebind is done.</returns>
    /// <exception cref="ObjectDisposedException">The listener is disposed.</exception>
    public async Task RebindAsync(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _rebinding.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await RebindCoreAsync(cancellation).ConfigureAwait(false);
        }
        finally
        {
            _rebinding.Release();
        }
    }

    /// <summary>
    /// Stops the accept loop and the VM watch, closes the socket, and disposes every live channel. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        Socket? socket;
        lock (_gate)
        {
            socket = _socket;
            _socket = null;
        }

        socket?.Dispose();
        WaypipeChannel[] open;
        lock (_live)
        {
            open = [.. _live];
            _live.Clear();
        }

        _unclaimed.Clear();
        foreach (var channel in open)
        {
            channel.Dispose();
        }

        DisposeEnded();
    }

    private static HyperVWaypipeListener Start(
        HyperVVm vm,
        Socket socket,
        HyperVEndPoint local,
        Action<Action> post,
        WaypipeCompression compression,
        WaypipeLimits? limits,
        WaypipeChannelOptions? options)
    {
        var listener = new HyperVWaypipeListener(vm, socket, local, post, compression, limits, options);
        WaypipeLog.Log.Info($"listening on {local} VmId={local.VmId} ({vm}) ServiceId={local.ServiceId}");
        _ = listener.AcceptLoopAsync(socket);
        if (vm.Kind == HyperVVmKind.Wsl)
        {
            _ = listener.WatchAsync();
        }

        return listener;
    }

    private static async Task<Guid> ResolveAsync(HyperVVm vm, CancellationToken cancellation) =>
        vm.Kind == HyperVVmKind.Wsl ? await WslVm.QueryIdAsync(cancellation).ConfigureAwait(false) : vm.Id;

    private static Socket Open(HyperVEndPoint local, HyperVVm vm)
    {
        Socket socket;
        try
        {
            socket = new Socket(HyperVEndPoint.HyperVFamily, SocketType.Stream, HyperVEndPoint.RawProtocol);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressFamilyNotSupported or SocketError.ProtocolNotSupported)
        {
            throw new IOException("Hyper-V sockets need Windows 10 1607 or later with the Hyper-V or WSL platform present", ex);
        }

        try
        {
            socket.Bind(local);
            socket.Listen(Backlog);
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            throw ex.SocketErrorCode switch
            {
                SocketError.AddressAlreadyInUse => new IOException($"{local} is already taken on VmId {local.VmId}", ex),
                SocketError.AccessDenied => new IOException(
                    $"binding {local} on VmId {local.VmId} was refused: that VM accepts no listener for a service with no GuestCommunicationServices key, "
                    + $"elevated or not; run scripts/hyperv-register-service.ps1 -Port {local.VsockPort} elevated{BracedNote(local.ServiceId)}",
                    ex),
                SocketError.AddressNotAvailable => new IOException(
                    $"VmId {local.VmId} names no running VM, so {local} cannot be bound on it; start the VM, or for WSL let the listener query the id again",
                    ex),
                SocketError.AddressFamilyNotSupported => new IOException(
                    "Hyper-V sockets need Windows 10 1607 or later with the Hyper-V or WSL platform present", ex),
                _ => new IOException($"binding {local} on VmId {local.VmId} failed: {ex.Message}", ex),
            };
        }

        if (vm.Kind == HyperVVmKind.Fixed &&
            vm.Id != HyperVEndPoint.Loopback &&
            vm.Id != HyperVEndPoint.Parent &&
            !HyperVRegistry.IsRegistered(local.ServiceId))
        {
            WaypipeLog.Log.Warn(
                $"{local} on VmId {vm} has no GuestCommunicationServices key, so a full Hyper-V guest cannot reach it; "
                + $"run scripts/hyperv-register-service.ps1 -Port {local.VsockPort} elevated{BracedNote(local.ServiceId)}. A WSL2 guest reaches only a bind on its own VM; use --hyperv-vm wsl");
        }

        return socket;
    }

    private static string BracedNote(Guid serviceId) =>
        HyperVRegistry.IsRegisteredWithBraces(serviceId)
            ? $"; the key {{{serviceId}}} exists, but Hyper-V matches only the name without braces"
            : string.Empty;

    private Socket? Current
    {
        get
        {
            lock (_gate)
            {
                return _socket;
            }
        }
    }

    private async Task AcceptLoopAsync(Socket socket)
    {
        while (!_disposed && ReferenceEquals(Current, socket))
        {
            Socket accepted;
            try
            {
                accepted = await socket.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException) when (_disposed || !ReferenceEquals(Current, socket))
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Interrupted)
            {
                continue;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TooManyOpenSockets or SocketError.NoBufferSpaceAvailable)
            {
                WaypipeLog.Log.Warn($"accepting on {LocalEndPoint} paused for {RetryMillis} ms: {ex.Message}");
                try
                {
                    await Task.Delay(RetryMillis, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }
            catch (SocketException ex)
            {
                if (_vm.Kind == HyperVVmKind.Wsl)
                {
                    WaypipeLog.Log.Warn($"accepting on {LocalEndPoint} VmId={LocalEndPoint.VmId} failed ({ex.SocketErrorCode}); rebinding");
                    _ = RebindInBackgroundAsync();
                }
                else
                {
                    Fail(ex);
                }

                return;
            }

            Attach(accepted);
        }
    }

    private void Attach(Socket accepted)
    {
        var index = Interlocked.Increment(ref _accepted);
        var remote = accepted.RemoteEndPoint;
        var stream = new NetworkStream(accepted, ownsSocket: true);
        WaypipeChannel channel;
        try
        {
            channel = WaypipeChannel.AttachAccepted(
                stream,
                () =>
                {
                    try
                    {
                        accepted.Shutdown(SocketShutdown.Both);
                    }
                    catch (SocketException)
                    {
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                },
                _compression,
                _limits,
                _options,
                attached =>
                {
                    lock (_live)
                    {
                        _live.Add(attached);
                    }

                    attached.Ended += _ => OnEnded(attached);
                });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stream.Dispose();
            WaypipeLog.Log.Warn($"channel {index} on {LocalEndPoint} could not start: {ex.Message}");
            return;
        }

        WaypipeLog.Log.Debug($"channel {index} accepted on {LocalEndPoint} from VmId={(remote as HyperVEndPoint)?.VmId} {remote}");
        _post(() =>
        {
            if (_disposed)
            {
                channel.Dispose();
                return;
            }

            if (_channelAccepted is { } accepted)
            {
                accepted(channel);
            }
            else
            {
                _unclaimed.Add(channel);
            }
        });
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
            _post(DisposeEnded);
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

    private async Task RebindInBackgroundAsync()
    {
        try
        {
            if (!await _rebinding.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (!await WslVm.IsRunningAsync(_stop.Token).ConfigureAwait(false))
            {
                _vmStopped = true;
                WaypipeLog.Log.Info($"the WSL VM behind {LocalEndPoint} stopped; rebinding once it runs again");
                return;
            }

            Exception? last = null;
            foreach (var delay in RebindBackoff)
            {
                await Task.Delay(delay, _stop.Token).ConfigureAwait(false);
                try
                {
                    await RebindCoreAsync(_stop.Token).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (ex is IOException or WslVmException or SocketException)
                {
                    last = ex;
                    WaypipeLog.Log.Warn($"rebinding {LocalEndPoint} failed: {ex.Message}");
                }
            }

            Fail(last!);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WslVmException ex)
        {
            Fail(ex);
        }
        finally
        {
            ReleaseRebinding();
        }
    }

    private void ReleaseRebinding()
    {
        try
        {
            _rebinding.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RebindCoreAsync(CancellationToken cancellation)
    {
        var old = LocalEndPoint;
        lock (_gate)
        {
            _socket?.Dispose();
            _socket = null;
        }

        var id = await ResolveAsync(_vm, cancellation).ConfigureAwait(false);
        var local = HyperVEndPoint.ForVsockPort(id, _port);
        var socket = Open(local, _vm);
        lock (_gate)
        {
            if (_disposed)
            {
                socket.Dispose();
                return;
            }

            _socket = socket;
            _local = local;
        }

        _vmStopped = false;
        WaypipeLog.Log.Info($"rebound {local} on WSL VM {old.VmId} -> {local.VmId}");
        _ = AcceptLoopAsync(socket);
        _post(() =>
        {
            if (!_disposed)
            {
                Rebound?.Invoke(local);
            }
        });
    }

    private async Task WatchAsync()
    {
        var stoppedAt = 0L;
        try
        {
            while (!_disposed)
            {
                var interval = WatchInterval(stoppedAt);
                await Task.Delay(interval, _stop.Token).ConfigureAwait(false);
                bool running;
                try
                {
                    running = await WslVm.IsRunningAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (WslVmException ex)
                {
                    WaypipeLog.Log.Debug($"watching the WSL VM: {ex.Message}");
                    continue;
                }

                if (!running)
                {
                    if (stoppedAt == 0)
                    {
                        stoppedAt = Environment.TickCount64;
                    }

                    if (!_vmStopped)
                    {
                        _vmStopped = true;
                        WaypipeLog.Log.Info($"the WSL VM behind {LocalEndPoint} stopped; watching for it to run again");
                    }

                    continue;
                }

                var wasStopped = _vmStopped;
                Guid id;
                try
                {
                    id = await WslVm.QueryIdAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (WslVmException ex)
                {
                    WaypipeLog.Log.Debug($"watching the WSL VM: {ex.Message}");
                    continue;
                }

                if (wasStopped || id != LocalEndPoint.VmId)
                {
                    stoppedAt = 0;
                    _vmStopped = true;
                    _ = RebindInBackgroundAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal static TimeSpan WatchInterval(long stoppedAt) =>
        stoppedAt != 0 && Environment.TickCount64 - stoppedAt < 60_000 ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);

    private void Fail(Exception error)
    {
        WaypipeLog.Log.Error($"accepting on {LocalEndPoint} failed: {error.Message}");
        lock (_gate)
        {
            _socket?.Dispose();
            _socket = null;
        }

        _post(() =>
        {
            if (!_disposed)
            {
                Failed?.Invoke(error);
            }
        });
    }
}
