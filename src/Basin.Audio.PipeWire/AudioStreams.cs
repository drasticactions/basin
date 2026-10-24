using Basin.Diagnostics;
using System.Globalization;
using PipeWire;
using PipeWire.Native;
using PipeWire.Spa;

namespace Basin.Audio.PipeWire;

/// <summary>
/// Binds every <c>Stream/Output/Audio</c> node and its client, and reports each one as an <see cref="AudioStream"/>. It skips the voices and players of the session. Only this class allocates per stream.
/// </summary>
public sealed class AudioStreams : IDisposable
{
    /// <summary>
    /// The node property that marks a node of a voice or player. A stream that shows it with the value <c>true</c> is removed.
    /// </summary>
    public const string VoiceKey = "basin.voice";

    /// <summary>
    /// The property that carries a launch tag, read into <see cref="AudioStream.LaunchId"/>. A consumer puts it in the environment of the programs it launches.
    /// </summary>
    public const string LaunchKey = "basin.launch.id";

    private const string StreamClass = "Stream/Output/Audio";

    private readonly AudioSession _session;
    private readonly Dictionary<uint, Pending> _nodes = [];
    private readonly Dictionary<uint, ClientEntry> _clients = [];
    private readonly Dictionary<uint, AudioStream> _streams = [];
    private readonly SpaPodBuilder _builder = new(256);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioStreams"/> class. It binds the streams that already exist and follows the registry of <paramref name="session"/> until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="session">The session to watch.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public AudioStreams(AudioSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _session.GlobalAdded += OnGlobalAdded;
        _session.GlobalRemoved += OnGlobalRemoved;
        foreach (var global in session.Globals.Values)
        {
            OnGlobalAdded(global);
        }

        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the streams whose node and client properties are read. A stream is in the collection before <see cref="Added"/> is raised.
    /// </summary>
    public IReadOnlyCollection<AudioStream> Streams => _streams.Values;

    /// <summary>
    /// Occurs when the properties of a new stream and its client are read for the first time.
    /// </summary>
    public event Action<AudioStream>? Added;

    /// <summary>
    /// Occurs when a known stream or its client sends new properties, or when the stream starts or stops running. The argument is a new <see cref="AudioStream"/> that replaces the old one.
    /// </summary>
    public event Action<AudioStream>? Changed;

    /// <summary>
    /// Occurs when the node of a reported stream goes away, or when it later shows <see cref="VoiceKey"/>.
    /// </summary>
    public event Action<AudioStream>? Removed;

    /// <summary>
    /// Finds a reported stream by its node id.
    /// </summary>
    /// <param name="nodeId">The PipeWire global id of the node.</param>
    /// <returns>The stream, or <see langword="null"/> when it is not reported.</returns>
    public AudioStream? Find(uint nodeId) => _streams.GetValueOrDefault(nodeId);

    /// <summary>
    /// Writes the mute of one stream node through its props. The node keeps its volume. Nothing reports the new value back.
    /// </summary>
    /// <param name="nodeId">The PipeWire global id of a reported stream.</param>
    /// <param name="muted"><see langword="true"/> to mute the stream.</param>
    /// <returns><see langword="true"/> when the write was sent. <see langword="false"/> when the object is disposed or the stream is not reported.</returns>
    public bool SetMuted(uint nodeId, bool muted)
    {
        if (_disposed || !_streams.ContainsKey(nodeId) || !_nodes.TryGetValue(nodeId, out var pending))
        {
            return false;
        }

        _builder.Reset();
        SinkVolume.BuildProps(_builder, [], muted);
        pending.Node.SetParam(spa_param_type.SPA_PARAM_Props, _builder.WrittenSpan);
        return true;
    }

    /// <summary>
    /// Stops following the registry and destroys every node and client proxy. It raises no <see cref="Removed"/>. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.GlobalAdded -= OnGlobalAdded;
        _session.GlobalRemoved -= OnGlobalRemoved;
        foreach (var pending in _nodes.Values)
        {
            pending.Node.Dispose();
        }

        foreach (var client in _clients.Values)
        {
            client.Proxy.Dispose();
        }

        _nodes.Clear();
        _clients.Clear();
        _streams.Clear();
        _builder.Dispose();
        BasinCounters.Untrack();
    }

    internal static AudioStream Describe(uint nodeId, IReadOnlyDictionary<string, string?> node, IReadOnlyDictionary<string, string?>? client, bool running = false)
    {
        string? Get(string key) => node.TryGetValue(key, out var value) && value is { Length: > 0 }
            ? value
            : client is not null && client.TryGetValue(key, out var fromClient) && fromClient is { Length: > 0 } ? fromClient : null;

        var api = Get("client.api");
        return new AudioStream(nodeId)
        {
            Serial = ULong(Get("object.serial")),
            ClientId = (uint)ULong(Get("client.id")),
            VerifiedPid = client is not null && client.TryGetValue("pipewire.sec.pid", out var pid) ? Int(pid) : 0,
            ReportedPid = Int(Get("application.process.id")),
            FlatpakAppId = Get("pipewire.access.portal.app_id"),
            ApplicationName = Get("application.name"),
            MediaRole = Get("media.role"),
            ViaPulse = api == "pipewire-pulse",
            LaunchId = (long)ULong(Get(LaunchKey)),
            IsRunning = running,
        };
    }

    private static ulong ULong(string? text) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static int Int(string? text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private void OnGlobalAdded(PipeWireGlobal global)
    {
        if (!global.IsNode || global.GetProperty("media.class") != StreamClass || _nodes.ContainsKey(global.Id) ||
            _session.IsVoiceNode(global.GetProperty("node.name")))
        {
            return;
        }

        var node = _session.Registry.BindNode(global.Id);
        var pending = new Pending(global.Id, node);
        _nodes[global.Id] = pending;
        node.Info += (_, info) =>
        {
            var running = info.State == pw_node_state.PW_NODE_STATE_RUNNING;
            if (info.Properties.Count == 0 && running == pending.Running)
            {
                return;
            }

            pending.Running = running;
            if (info.Properties.Count > 0)
            {
                pending.Properties = info.Properties;
            }

            Resolve(pending);
        };
    }

    private void OnGlobalRemoved(uint id)
    {
        if (_clients.Remove(id, out var client))
        {
            client.Proxy.Dispose();
            return;
        }

        if (_nodes.Remove(id, out var pending))
        {
            pending.Node.Dispose();
            if (pending.ClientId != 0)
            {
                ReleaseClient(pending.ClientId);
            }

            if (_streams.Remove(id, out var stream))
            {
                Removed?.Invoke(stream);
            }
        }
    }

    private void Resolve(Pending pending)
    {
        if (pending.Properties is not { } properties)
        {
            return;
        }

        if (properties.TryGetValue(VoiceKey, out var voice) && voice == "true")
        {
            if (_streams.Remove(pending.Id, out var published))
            {
                Removed?.Invoke(published);
            }

            return;
        }

        var clientId = (uint)ULong(properties.GetValueOrDefault("client.id"));
        if (clientId != 0 && pending.ClientId == 0)
        {
            pending.ClientId = clientId;
            AcquireClient(clientId, pending);
        }

        IReadOnlyDictionary<string, string?>? client = null;
        if (clientId != 0 && _clients.TryGetValue(clientId, out var entry))
        {
            if (entry.Properties is null && !entry.Failed)
            {
                return;
            }

            client = entry.Properties;
        }

        var stream = Describe(pending.Id, properties, client, pending.Running);
        var added = !_streams.ContainsKey(pending.Id);
        _streams[pending.Id] = stream;
        if (added)
        {
            Added?.Invoke(stream);
        }
        else
        {
            Changed?.Invoke(stream);
        }
    }

    private void AcquireClient(uint clientId, Pending waiter)
    {
        if (_clients.TryGetValue(clientId, out var entry))
        {
            entry.References++;
            entry.Waiters.Add(waiter);
            return;
        }

        PipeWireClient proxy;
        try
        {
            proxy = _session.Registry.BindClient(clientId);
        }
        catch (PipeWireException)
        {
            return;
        }

        entry = new ClientEntry(proxy) { References = 1 };
        entry.Waiters.Add(waiter);
        _clients[clientId] = entry;
        proxy.Info += (_, info) =>
        {
            if (info.Properties.Count == 0)
            {
                return;
            }

            entry.Properties = info.Properties;
            foreach (var node in entry.Waiters.ToArray())
            {
                Resolve(node);
            }
        };
        proxy.Error += (_, _) =>
        {
            entry.Failed = true;
            foreach (var node in entry.Waiters.ToArray())
            {
                Resolve(node);
            }
        };
    }

    private void ReleaseClient(uint clientId)
    {
        if (!_clients.TryGetValue(clientId, out var entry))
        {
            return;
        }

        entry.Waiters.RemoveAll(waiter => !_nodes.ContainsKey(waiter.Id));
        entry.References--;
    }

    private sealed class Pending(uint id, PipeWireNode node)
    {
        public uint Id { get; } = id;

        public PipeWireNode Node { get; } = node;

        public uint ClientId { get; set; }

        public bool Running { get; set; }

        public IReadOnlyDictionary<string, string?>? Properties { get; set; }
    }

    private sealed class ClientEntry(PipeWireClient proxy)
    {
        public PipeWireClient Proxy { get; } = proxy;

        public int References { get; set; }

        public bool Failed { get; set; }

        public List<Pending> Waiters { get; } = [];

        public IReadOnlyDictionary<string, string?>? Properties { get; set; }
    }
}
