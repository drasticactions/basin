using Basin;
using Basin.Hosted;
using Basin.Transport.Waypipe;
using Wayland.Server;

namespace Tarn;

public sealed class TarnSession : IDisposable
{
    private readonly BasinCompositorHost _host;
    private readonly Action<Action> _post;
    private readonly Func<CancellationToken, Task<Stream>> _open;
    private readonly WaypipeChannelOptions? _options;
    private readonly List<WaypipeChannel> _channels = [];
    private readonly Dictionary<WaypipeChannel, WlClient> _clients = [];
    private readonly CancellationTokenSource _closing = new();
    private bool _disposed;
    private bool _filtered;
    private LinuxDmabufGlobal? _dmabuf;

    public TarnSession(
        BasinCompositorHost host,
        Action<Action> postToCompositor,
        Func<CancellationToken, Task<Stream>> open,
        WaypipeChannelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(postToCompositor);
        ArgumentNullException.ThrowIfNull(open);
        _host = host;
        _post = postToCompositor;
        _open = open;
        _options = options;
    }

    public int Clients { get; private set; }

    public event Action? Changed;

    public event Action<Exception?>? Ended;

    public Task StartAsync() => OpenSpareAsync(first: true);

    private async Task OpenSpareAsync(bool first = false)
    {
        if (_disposed)
        {
            return;
        }

        Stream stream;
        try
        {
            stream = await _open(_closing.Token).ConfigureAwait(true);
        }
        catch (Exception ex) when (!_disposed && !first)
        {
            Ended?.Invoke(ex);
            return;
        }

        if (_disposed)
        {
            stream.Dispose();
            return;
        }

        WaypipeChannel channel = null!;
        channel = WaypipeChannel.AttachChannel(stream, WaypipeCompression.Lz4, options: _options, configure: attached =>
        {
            channel = attached;
            _channels.Add(attached);
            attached.Opened += () =>
            {
                Clients++;
                Changed?.Invoke();
                _ = OpenSpareAsync();
            };
            attached.Ended += failure =>
            {
                var carried = _channels.Remove(attached);
                if (carried && Clients > 0)
                {
                    Clients--;
                }

                Changed?.Invoke();
                if (_channels.Count == 0 && !_disposed)
                {
                    Ended?.Invoke(failure);
                }
            };
        });
        _post(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (!_filtered)
            {
                _filtered = true;
                _host.Display.SetGlobalFilter((_, _, name) => channel.Globals.Carries(name));
                if (channel.Options.CarriesDmabuf && _host.Dmabuf is null)
                {
                    _dmabuf = new LinuxDmabufGlobal(
                        _host.Display,
                        _host.Services.Require<ClientBufferRegistry>(),
                        channel.Globals.Formats,
                        WaypipeGlobals.SyntheticMainDevice,
                        compositor: _host.Services.Require<CompositorGlobal>());
                }
            }

            var client = _host.Display.CreateClient(channel.Transport);
            _clients[channel] = client;
            client.Destroyed += () => _clients.Remove(channel);
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _closing.Cancel();
        foreach (var channel in _channels.ToArray())
        {
            channel.Dispose();
        }

        _channels.Clear();
        _post(() =>
        {
            foreach (var client in _clients.Values.ToArray())
            {
                client.Destroy();
            }

            _clients.Clear();
            _host.Display.SetGlobalFilter(null);
            _dmabuf?.Dispose();
            _dmabuf = null;
        });
        _closing.Dispose();
    }
}
