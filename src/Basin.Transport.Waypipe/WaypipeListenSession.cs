using Wayland.Server;

namespace Basin.Transport.Waypipe;

/// <summary>
/// A listener and the adoption of its channels as clients of one display, for a host that posts work to its compositor thread.
/// </summary>
public sealed class WaypipeListenSession : IDisposable
{
    private readonly WlServerDisplay _display;
    private readonly ClientBufferRegistry _buffers;
    private readonly CompositorGlobal _compositor;
    private readonly Func<bool> _hostHasDmabuf;
    private readonly Action<Action> _post;
    private readonly IWaypipeListener _listener;
    private bool _disposed;
    private WaypipeAdoption? _adoption;

    private WaypipeListenSession(
        WlServerDisplay display,
        ClientBufferRegistry buffers,
        CompositorGlobal compositor,
        Func<bool> hostHasDmabuf,
        Action<Action> post,
        IWaypipeListener listener)
    {
        _display = display;
        _buffers = buffers;
        _compositor = compositor;
        _hostHasDmabuf = hostHasDmabuf;
        _post = post;
        _listener = listener;
        _listener.ChannelAccepted += OnChannelAccepted;
        _listener.Failed += error => Ended?.Invoke(error);
        if (OperatingSystem.IsWindows() && listener is HyperVWaypipeListener hyperV)
        {
            VmId = hyperV.LocalEndPoint.VmId;
            IsWsl = hyperV.Vm.Kind == HyperVVmKind.Wsl;
            hyperV.Rebound += local =>
            {
                VmId = local.VmId;
                Rebound?.Invoke(local.VmId);
            };
        }
    }

    /// <summary>
    /// Gets the listener that the session owns.
    /// </summary>
    public IWaypipeListener Listener => _listener;

    /// <summary>
    /// Gets the Hyper-V VM id that the listener binds, or <see langword="null"/> off Windows. It changes after a rebind.
    /// </summary>
    public Guid? VmId { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the listener follows the WSL2 VM.
    /// </summary>
    public bool IsWsl { get; private set; }

    /// <summary>
    /// Gets a short label for <see cref="VmId"/> to show to a user, or <see langword="null"/> when there is no VM id.
    /// </summary>
    public string? VmLabel => VmId is { } id ? IsWsl ? $"WSL VM {id}" : $"VmId {id}" : null;

    /// <summary>
    /// Gets the number of channels that answered their connection header and did not end.
    /// </summary>
    public int Clients { get; private set; }

    /// <summary>
    /// Occurs on the compositor thread when a channel opens or ends.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Occurs when the listener fails, with the error that stopped it.
    /// </summary>
    public event Action<Exception?>? Ended;

    /// <summary>
    /// Occurs on the compositor thread when a Hyper-V listener binds the new VM id after the WSL2 VM restarted.
    /// </summary>
    public event Action<Guid>? Rebound;

    /// <summary>
    /// Binds a listener on the compositor thread and starts to adopt the channels that it accepts.
    /// </summary>
    /// <param name="display">The display that gets the remote clients.</param>
    /// <param name="buffers">The buffer registry for a substitute dmabuf global.</param>
    /// <param name="compositor">The compositor global for a substitute dmabuf global.</param>
    /// <param name="hostHasDmabuf">A function that tells, at the first channel, whether the display already has a dmabuf global.</param>
    /// <param name="loop">The compositor loop for a listener with descriptors.</param>
    /// <param name="postToCompositor">The delegate that runs an action on the compositor thread.</param>
    /// <param name="endpoint">The endpoint as <see cref="WaypipeEndpoint.ForListening"/> reads it.</param>
    /// <param name="vm">The Hyper-V VM to bind on Windows, or <see langword="null"/> for the WSL2 VM.</param>
    /// <param name="options">The content that each channel accepts, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellation">Cancels a bind that has not completed.</param>
    /// <returns>A task that completes with the session, which the caller owns, or faults with the bind error.</returns>
    public static async Task<WaypipeListenSession> StartAsync(
        WlServerDisplay display,
        ClientBufferRegistry buffers,
        CompositorGlobal compositor,
        Func<bool> hostHasDmabuf,
        ICompositorEventLoop loop,
        Action<Action> postToCompositor,
        string endpoint,
        HyperVVm? vm = null,
        WaypipeChannelOptions? options = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(buffers);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(hostHasDmabuf);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(postToCompositor);
        var started = new TaskCompletionSource<WaypipeListenSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        postToCompositor(async () =>
        {
            IWaypipeListener listener;
            try
            {
                listener = await WaypipeListeners.BindAsync(
                    endpoint,
                    new WaypipeListenerHost(loop, postToCompositor),
                    vm,
                    options: options,
                    cancellation: cancellation).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                started.SetException(error);
                return;
            }

            postToCompositor(() => started.SetResult(
                new WaypipeListenSession(display, buffers, compositor, hostHasDmabuf, postToCompositor, listener)));
        });

        return await started.Task.ConfigureAwait(true);
    }

    private void OnChannelAccepted(WaypipeChannel channel)
    {
        if (_disposed)
        {
            channel.Dispose();
            return;
        }

        var carried = false;
        channel.Opened += () => _post(() =>
        {
            if (_disposed)
            {
                return;
            }

            carried = true;
            Clients++;
            Changed?.Invoke();
        });
        channel.Ended += _ => _post(() =>
        {
            if (carried && Clients > 0)
            {
                Clients--;
            }

            Changed?.Invoke();
        });
        _adoption ??= new WaypipeAdoption(_display, _buffers, _compositor, _hostHasDmabuf());
        _adoption.Adopt(channel);
    }

    /// <summary>
    /// Posts the disposal of the listener and of every adopted client to the compositor thread. Channels that arrive later are disposed at once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _post(() =>
        {
            _listener.Dispose();
            _adoption?.Dispose();
            _adoption = null;
        });
    }
}
