using Basin;
using Basin.Hosted;
using Basin.Transport.Waypipe;

namespace PortComp;

public sealed class PortCompListenSession : IDisposable
{
    private readonly BasinCompositorHost _host;
    private readonly Action<Action> _post;
    private readonly IWaypipeListener _listener;
    private bool _disposed;
    private WaypipeAdoption? _adoption;

    private PortCompListenSession(BasinCompositorHost host, Action<Action> post, IWaypipeListener listener)
    {
        _host = host;
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

    public IWaypipeListener Listener => _listener;

    public Guid? VmId { get; private set; }

    public bool IsWsl { get; private set; }

    public string? VmLabel => VmId is { } id ? IsWsl ? $"WSL VM {id}" : $"VmId {id}" : null;

    public int Clients { get; private set; }

    public event Action? Changed;

    public event Action<Exception?>? Ended;

    public event Action<Guid>? Rebound;

    public static async Task<PortCompListenSession> StartAsync(
        BasinCompositorHost host,
        Action<Action> postToCompositor,
        string endpoint,
        HyperVVm? vm = null,
        WaypipeChannelOptions? options = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(postToCompositor);
        var started = new TaskCompletionSource<PortCompListenSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        postToCompositor(async () =>
        {
            IWaypipeListener listener;
            try
            {
                listener = await WaypipeListeners.BindAsync(
                    endpoint,
                    new WaypipeListenerHost(host.Loop, postToCompositor),
                    vm,
                    options: options,
                    cancellation: cancellation).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                started.SetException(error);
                return;
            }

            postToCompositor(() => started.SetResult(new PortCompListenSession(host, postToCompositor, listener)));
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
        _adoption ??= new WaypipeAdoption(
            _host.Display,
            _host.Services.Require<ClientBufferRegistry>(),
            _host.Services.Require<CompositorGlobal>(),
            _host.Dmabuf is not null);
        _adoption.Adopt(channel);
    }

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
