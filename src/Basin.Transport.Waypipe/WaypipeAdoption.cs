using Wayland.Server;

namespace Basin.Transport.Waypipe;

/// <summary>
/// Turns waypipe channels into Wayland clients of one display and hides the globals that each channel cannot carry. Use it on the compositor thread.
/// </summary>
public sealed class WaypipeAdoption : IDisposable
{
    private const string DmabufInterface = "zwp_linux_dmabuf_v1";

    private readonly WlServerDisplay _display;
    private readonly ClientBufferRegistry _buffers;
    private readonly CompositorGlobal? _compositor;
    private readonly bool _hostHasDmabuf;
    private readonly WlServerDisplay.GlobalFilter? _inner;
    private readonly Dictionary<WlClient, WaypipeChannel> _clients = [];
    private LinuxDmabufGlobal? _synthetic;
    private bool _filtering;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaypipeAdoption"/> class. The global filter is installed at the first <see cref="Adopt"/>.
    /// </summary>
    /// <param name="display">The display that gets the remote clients.</param>
    /// <param name="buffers">The buffer registry for the substitute dmabuf global.</param>
    /// <param name="compositor">The compositor global that the substitute dmabuf global uses, or <see langword="null"/>.</param>
    /// <param name="hostHasDmabuf">A value indicating whether the display already has a dmabuf global. When <see langword="false"/>, a channel with dmabuf gets a substitute global.</param>
    /// <param name="inner">An earlier global filter that still applies first, or <see langword="null"/>.</param>
    public WaypipeAdoption(
        WlServerDisplay display,
        ClientBufferRegistry buffers,
        CompositorGlobal? compositor,
        bool hostHasDmabuf,
        WlServerDisplay.GlobalFilter? inner = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(buffers);
        _display = display;
        _buffers = buffers;
        _compositor = compositor;
        _hostHasDmabuf = hostHasDmabuf;
        _inner = inner;
    }

    /// <summary>
    /// Gets the number of adopted clients that are not destroyed.
    /// </summary>
    public int Count => _clients.Count;

    /// <summary>
    /// Creates a Wayland client on the channel transport.
    /// </summary>
    /// <param name="channel">The channel to adopt. The caller keeps ownership of it.</param>
    /// <returns>The new client. Its destruction removes it from this adoption.</returns>
    public WlClient Adopt(WaypipeChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!_filtering)
        {
            _filtering = true;
            _display.SetGlobalFilter(Visible);
        }

        if (channel.Options.CarriesDmabuf && !_hostHasDmabuf && _synthetic is null)
        {
            _synthetic = new LinuxDmabufGlobal(
                _display,
                _buffers,
                channel.Globals.Formats,
                WaypipeGlobals.SyntheticMainDevice,
                compositor: _compositor);
        }

        var client = _display.CreateClient(channel.Transport);
        _clients[client] = channel;
        client.Destroyed += () => _clients.Remove(client);
        return client;
    }

    /// <summary>
    /// Destroys every adopted client, restores the earlier global filter and removes the substitute dmabuf global.
    /// </summary>
    public void Dispose()
    {
        foreach (var client in _clients.Keys.ToArray())
        {
            client.Destroy();
        }

        _clients.Clear();
        if (_filtering)
        {
            _filtering = false;
            _display.SetGlobalFilter(_inner);
        }

        _synthetic?.Dispose();
        _synthetic = null;
    }

    internal bool Visible(WlClient client, WlGlobal? global, string interfaceName)
    {
        if (_inner is not null && !_inner(client, global, interfaceName))
        {
            return false;
        }

        return _clients.TryGetValue(client, out var channel)
            ? channel.Globals.Carries(interfaceName)
            : _synthetic is null || interfaceName != DmabufInterface;
    }
}
