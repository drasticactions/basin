using Basin.Diagnostics;
using PipeWire;
using PipeWire.Native;
using static Basin.Audio.PipeWire.AudioLog;

namespace Basin.Audio.PipeWire;

/// <summary>
/// One PipeWire connection whose loop runs on the compositor loop, so every PipeWire callback runs on the compositor thread. It tracks the registry and the subject-0 keys of the <c>default</c> metadata.
/// </summary>
public sealed unsafe class AudioSession : IDisposable
{
    private const string DefaultMetadataName = "default";

    private readonly ICompositorEventLoop _loop;
    private readonly PipeWireLoop _pwLoop;
    private readonly PipeWireContext _context;
    private readonly PipeWireCore _core;
    private readonly PipeWireRegistry _registry;
    private readonly IEventSource _fdSource;
    private readonly Dictionary<uint, PipeWireGlobal> _globals = [];
    private readonly HashSet<string> _voiceNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _defaults = new(StringComparer.Ordinal);
    private PipeWireMetadata? _metadata;
    private uint _metadataId = uint.MaxValue;
    private int _pendingSync = -1;
    private bool _synced;
    private bool _disposed;

    private AudioSession(ICompositorEventLoop loop, PipeWireLoop pwLoop, PipeWireContext context, PipeWireCore core, string remote)
    {
        _loop = loop;
        _pwLoop = pwLoop;
        _context = context;
        _core = core;
        Remote = remote;
        _core.Done += (_, done) =>
        {
            if (done.Sequence == _pendingSync)
            {
                _synced = true;
            }
        };
        _core.Error += (_, error) => Log.Warn($"PipeWire error on object {error.Id}: {error.Message} ({error.Result})");
        _registry = _core.GetRegistry();
        _registry.GlobalAdded += (_, global) => OnGlobalAdded(global);
        _registry.GlobalRemoved += (_, id) => OnGlobalRemoved(id);
        _fdSource = loop.AddFd(pwLoop.Fd, FdReadiness.Readable, (_, _) => Pump(0));
        BasinCounters.Track();
    }

    /// <summary>
    /// Connects to a PipeWire daemon and runs one roundtrip. It never throws for a missing library or daemon, so a compositor without PipeWire runs as before.
    /// </summary>
    /// <param name="loop">The compositor loop that gets the PipeWire loop fd.</param>
    /// <param name="reason">Why the connection failed, or <see langword="null"/> on success.</param>
    /// <param name="remote">The remote name. The default is <c>PIPEWIRE_REMOTE</c>, else <c>pipewire-0</c>.</param>
    /// <param name="clientName">The <c>application.name</c> and <c>node.name</c> of the client. The default is <c>basin</c>.</param>
    /// <returns>The session, or <see langword="null"/> when libpipewire does not load or no daemon answers within 2 seconds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> is <see langword="null"/>.</exception>
    public static AudioSession? TryCreate(ICompositorEventLoop loop, out string? reason, string? remote = null, string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        if (!LibraryLoads(out reason))
        {
            return null;
        }

        remote ??= Environment.GetEnvironmentVariable("PIPEWIRE_REMOTE") is { Length: > 0 } fromEnvironment ? fromEnvironment : "pipewire-0";
        PipeWireLoop? pwLoop = null;
        PipeWireContext? context = null;
        try
        {
            PipeWireLibrary.Init();
            pwLoop = PipeWireLoop.Create();
            context = new PipeWireContext(pwLoop);
            using var properties = PipeWireProperties.From(
                "remote.name", remote,
                "application.name", clientName ?? "basin",
                "node.name", clientName ?? "basin");
            var core = context.Connect(properties);
            var session = new AudioSession(loop, pwLoop, context, core, remote);
            if (!session.Roundtrip())
            {
                session.Dispose();
                reason = $"the PipeWire daemon at {remote} did not answer";
                return null;
            }

            reason = null;
            return session;
        }
        catch (Exception error) when (error is DllNotFoundException or PipeWireException or EntryPointNotFoundException)
        {
            context?.Dispose();
            pwLoop?.Dispose();
            reason = error.Message;
            return null;
        }
    }

    /// <summary>
    /// Gets the remote name that the session connected to. Each voice and player passes it on, so it reaches the same daemon.
    /// </summary>
    public string Remote { get; }

    /// <summary>
    /// Gets the compositor loop that dispatches the session.
    /// </summary>
    public ICompositorEventLoop Loop => _loop;

    /// <summary>
    /// Gets the PipeWire context. The session owns it and destroys it on <see cref="Dispose"/>.
    /// </summary>
    public PipeWireContext Context => _context;

    /// <summary>
    /// Gets the core connection of the session. The session owns it.
    /// </summary>
    public PipeWireCore Core => _core;

    /// <summary>
    /// Gets the registry of the core. Use it to bind proxies. The session owns it.
    /// </summary>
    public PipeWireRegistry Registry => _registry;

    /// <summary>
    /// Gets the bound <c>default</c> metadata object, or <see langword="null"/> when the daemon has none or it has gone.
    /// </summary>
    public PipeWireMetadata? DefaultMetadata => _metadata;

    /// <summary>
    /// Gets every registry global that the session has seen, keyed by global id. A removed global leaves the table before <see cref="GlobalRemoved"/> is raised.
    /// </summary>
    public IReadOnlyDictionary<uint, PipeWireGlobal> Globals => _globals;

    /// <summary>
    /// Occurs after a registry global is added to <see cref="Globals"/>.
    /// </summary>
    public event Action<PipeWireGlobal>? GlobalAdded;

    /// <summary>
    /// Occurs after a registry global with the given id is removed from <see cref="Globals"/>.
    /// </summary>
    public event Action<uint>? GlobalRemoved;

    /// <summary>
    /// Occurs for each property event of the <c>default</c> metadata, for any subject. <see cref="DefaultProperty"/> already shows a subject-0 change when the event is raised.
    /// </summary>
    public event Action<PipeWireMetadataProperty>? MetadataChanged;

    /// <summary>
    /// Runs one iteration of the PipeWire loop and dispatches its callbacks. It does nothing after <see cref="Dispose"/>.
    /// </summary>
    /// <param name="timeoutMilliseconds">The longest time in milliseconds to wait for an event. 0 does not wait.</param>
    public void Pump(int timeoutMilliseconds)
    {
        if (_disposed)
        {
            return;
        }

        _pwLoop.Enter();
        try
        {
            _pwLoop.Iterate(timeoutMilliseconds);
        }
        finally
        {
            _pwLoop.Leave();
        }
    }

    /// <summary>
    /// Sends a sync to the daemon and pumps the loop until the answer arrives. Events that arrive meanwhile run their handlers.
    /// </summary>
    /// <param name="timeoutMilliseconds">The longest time in milliseconds to wait.</param>
    /// <returns><see langword="true"/> when the daemon answered in time.</returns>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    public bool Roundtrip(int timeoutMilliseconds = 2000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _synced = false;
        _pendingSync = _core.Sync();
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!_synced && Environment.TickCount64 < deadline)
        {
            Pump(20);
        }

        return _synced;
    }

    /// <summary>
    /// Pumps the loop until a condition is true. Events that arrive meanwhile run their handlers.
    /// </summary>
    /// <param name="condition">The test, evaluated before each pump.</param>
    /// <param name="timeoutMilliseconds">The longest time in milliseconds to wait.</param>
    /// <returns><see langword="true"/> when the condition became true in time.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is <see langword="null"/>.</exception>
    public bool WaitFor(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }

            Pump(20);
        }

        return true;
    }

    /// <summary>
    /// Reads a subject-0 key of the <c>default</c> metadata, for example <c>default.audio.sink</c>.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <returns>The last value that the session saw, or <see langword="null"/> when the key is not set.</returns>
    public string? DefaultProperty(string key) => _defaults.GetValueOrDefault(key);

    /// <summary>
    /// Tests whether a node name belongs to a live voice or player of this session.
    /// </summary>
    /// <param name="nodeName">The <c>node.name</c> to test, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a <see cref="SpatialVoice"/> or <see cref="SoundPlayer"/> of this session uses the name.</returns>
    public bool IsVoiceNode(string? nodeName) => nodeName is not null && _voiceNodes.Contains(nodeName);

    internal void AddVoiceNodes(string captureName, string playbackName)
    {
        _voiceNodes.Add(captureName);
        _voiceNodes.Add(playbackName);
    }

    internal void RemoveVoiceNodes(string captureName, string playbackName)
    {
        _voiceNodes.Remove(captureName);
        _voiceNodes.Remove(playbackName);
    }

    internal void AddOwnNode(string name) => _voiceNodes.Add(name);

    internal void RemoveOwnNode(string name) => _voiceNodes.Remove(name);

    /// <summary>
    /// Finds a node global by its <c>node.name</c>.
    /// </summary>
    /// <param name="nodeName">The name to look for.</param>
    /// <returns>The first matching node in <see cref="Globals"/>, or <see langword="null"/>.</returns>
    public PipeWireGlobal? FindNode(string nodeName)
    {
        foreach (var global in _globals.Values)
        {
            if (global.IsNode && global.GetProperty("node.name") == nodeName)
            {
                return global;
            }
        }

        return null;
    }

    /// <summary>
    /// Removes the loop fd and destroys the metadata proxy, the registry, the core, the context and the loop. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _fdSource.Remove();
        _metadata?.Dispose();
        _metadata = null;
        _registry.Dispose();
        _core.Dispose();
        _context.Dispose();
        _pwLoop.Dispose();
        _globals.Clear();
        _disposed = true;
        BasinCounters.Untrack();
    }

    private static bool LibraryLoads(out string? reason)
    {
        foreach (var candidate in (ReadOnlySpan<string>)["libpipewire-0.3.so.0", "libpipewire-0.3.so"])
        {
            if (System.Runtime.InteropServices.NativeLibrary.TryLoad(candidate, out var handle))
            {
                System.Runtime.InteropServices.NativeLibrary.Free(handle);
                reason = null;
                return true;
            }
        }

        reason = "libpipewire-0.3.so.0 is not installed";
        return false;
    }

    private void OnGlobalAdded(PipeWireGlobal global)
    {
        _globals[global.Id] = global;
        if (_metadata is null && global.Type == PipeWireInterfaces.Metadata && global.GetProperty("metadata.name") == DefaultMetadataName)
        {
            _metadataId = global.Id;
            _metadata = _registry.BindMetadata(global.Id);
            _metadata.PropertyChanged += (_, property) =>
            {
                if (property.Subject == 0 && property.Key is { } key)
                {
                    if (property.Value is { } value)
                    {
                        _defaults[key] = value;
                    }
                    else
                    {
                        _defaults.Remove(key);
                    }
                }
                else if (property.Subject == 0 && property.Key is null)
                {
                    _defaults.Clear();
                }

                MetadataChanged?.Invoke(property);
            };
        }

        GlobalAdded?.Invoke(global);
    }

    private void OnGlobalRemoved(uint id)
    {
        _globals.Remove(id);
        if (id == _metadataId)
        {
            _metadata?.Dispose();
            _metadata = null;
            _metadataId = uint.MaxValue;
        }

        GlobalRemoved?.Invoke(id);
    }
}
