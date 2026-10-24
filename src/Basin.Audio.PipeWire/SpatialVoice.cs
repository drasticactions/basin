using System.Diagnostics;
using System.Globalization;
using Basin.Diagnostics;
using PipeWire;
using PipeWire.Native;
using PipeWire.Spa;
using static Basin.Audio.PipeWire.AudioLog;
using static PipeWire.Native.Pipewire;

namespace Basin.Audio.PipeWire;

/// <summary>
/// One filter-chain module that places the streams attached to it. The session manager moves each attached stream onto the capture node of the voice. The voice never writes the volume of a stream.
/// </summary>
public sealed unsafe class SpatialVoice : IDisposable
{
    /// <summary>
    /// The step in degrees to which <see cref="Update"/> rounds the azimuth.
    /// </summary>
    public const float AzimuthQuantum = 2;

    /// <summary>
    /// The step in degrees to which <see cref="Update"/> rounds the elevation.
    /// </summary>
    public const float ElevationQuantum = 5;

    /// <summary>
    /// The step in decibels to which <see cref="Update"/> rounds the gain and the wet level.
    /// </summary>
    public const float GainQuantumDb = 0.25f;

    /// <summary>
    /// The step in octaves to which <see cref="Update"/> rounds the cutoff, on a grid anchored at <see cref="VoiceParams.OpenCutoffHz"/>.
    /// </summary>
    public const float CutoffQuantumOctaves = 1f / 12;

    /// <summary>
    /// The step in meters to which <see cref="Update"/> rounds the radius.
    /// </summary>
    public const float RadiusQuantum = 0.1f;

    private const string TargetObject = "target.object";

    private const string TargetNode = "target.node";

    private const int Freq = 0;
    private const int Gain = 1;
    private const int Left = 2;
    private const int Right = 3;
    private const int Azimuth = 4;
    private const int Elevation = 5;
    private const int Radius = 6;
    private const int WetLeft = 7;
    private const int WetRight = 8;

    private static readonly string[] ControlNames =
    [
        "occlude:Freq", "gain:Mult", "left:Mult", "right:Mult",
        "hrtf:Azimuth", "hrtf:Elevation", "hrtf:Radius", "mixL:Gain 2", "mixR:Gain 2",
    ];

    private readonly AudioSession _session;
    private readonly PipeWireModule _module;
    private readonly PipeWireNode _node;
    private readonly SpaPodBuilder _builder = new(512);
    private readonly IEventSource _timer;
    private readonly HashSet<uint> _streams = [];
    private readonly HashSet<uint> _awaitingEcho = [];
    private readonly float[] _sent = new float[ControlNames.Length];
    private readonly float[] _wanted = new float[ControlNames.Length];
    private readonly bool[] _dirty = new bool[ControlNames.Length];
    private readonly string _serialText;
    private readonly string _nodeText;
    private long _lastHrtf;
    private bool _hrtfPending;
    private bool _timerArmed;
    private bool _disposed;
    private bool _paramFailureLogged;

    private SpatialVoice(AudioSession session, VoiceGraph graph, string name, PipeWireModule module, PipeWireNode node, uint nodeId, ulong serial)
    {
        _session = session;
        Graph = graph;
        Name = name;
        _module = module;
        _node = node;
        CaptureNodeId = nodeId;
        CaptureSerial = serial;
        _serialText = serial.ToString(CultureInfo.InvariantCulture);
        _nodeText = nodeId.ToString(CultureInfo.InvariantCulture);
        Array.Fill(_sent, float.NaN);
        _timer = session.Loop.AddTimer(OnHrtfTimer);
        session.MetadataChanged += OnMetadataChanged;
        BasinCounters.Track();
    }

    /// <summary>
    /// Loads the filter chain and waits until its capture node appears. Do not call it inside a PipeWire callback, because it pumps the loop and handlers can run again.
    /// </summary>
    /// <param name="session">The session whose context loads the module.</param>
    /// <param name="graph">The graph to build.</param>
    /// <param name="name">The capture node name. The playback node takes the name with <c>.output</c> appended. Use a name that is unique to the process.</param>
    /// <param name="error">Why creation failed, or <see langword="null"/> on success.</param>
    /// <param name="description">The <c>node.description</c>, or <see langword="null"/> to use <paramref name="name"/>.</param>
    /// <returns>The voice, or <see langword="null"/> when the HRTF file is missing, the module does not load, or no capture node appears within 2 seconds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="graph"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty or white space.</exception>
    public static SpatialVoice? Create(AudioSession session, VoiceGraph graph, string name, out string? error, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (graph.Mode == VoiceMode.Hrtf && (graph.HrtfFile is not { Length: > 0 } file || !File.Exists(file)))
        {
            error = $"the HRTF file {graph.HrtfFile} does not exist";
            return null;
        }

        PipeWireModule module;
        session.AddVoiceNodes(name, name + ".output");
        try
        {
            module = session.Context.LoadModule("libpipewire-module-filter-chain", graph.Arguments(name, session.Remote, description ?? name));
        }
        catch (PipeWireException failure)
        {
            session.RemoveVoiceNodes(name, name + ".output");
            error = failure.Message;
            return null;
        }

        PipeWireGlobal? capture = null;
        if (!session.WaitFor(() => (capture = session.FindNode(name)) is not null))
        {
            module.Dispose();
            session.RemoveVoiceNodes(name, name + ".output");
            error = "the filter-chain module created no capture node";
            return null;
        }

        var serial = ulong.TryParse(capture!.GetProperty("object.serial"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var node = session.Registry.BindNode(capture.Id);
        error = null;
        return new SpatialVoice(session, graph, name, module, node, capture.Id, serial);
    }

    /// <summary>
    /// Gets the node name of the capture node.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the graph that the voice was built from.
    /// </summary>
    public VoiceGraph Graph { get; }

    /// <summary>
    /// Gets the PipeWire global id of the capture node.
    /// </summary>
    public uint CaptureNodeId { get; }

    /// <summary>
    /// Gets the <c>object.serial</c> of the capture node, which <see cref="Attach(uint)"/> writes as <c>target.object</c>.
    /// </summary>
    public ulong CaptureSerial { get; }

    /// <summary>
    /// Gets the node ids of the attached streams.
    /// </summary>
    public IReadOnlyCollection<uint> Streams => _streams;

    /// <summary>
    /// Gets the quantized parameters of the last <see cref="Update"/>. The radius is not yet clamped to the HRTF range.
    /// </summary>
    public VoiceParams Current { get; private set; } = VoiceParams.Centered;

    /// <summary>
    /// Gets or sets the shortest time between two HRTF control changes. Each change rebuilds the convolvers. A loop timer sends the last change that waited.
    /// </summary>
    public TimeSpan MinHrtfInterval { get; set; } = TimeSpan.FromSeconds(1.0 / 30);

    /// <summary>
    /// Gets the number of control writes that the voice sent to its node.
    /// </summary>
    public long ParamWrites { get; private set; }

    /// <summary>
    /// Gets the number of control writes that changed an HRTF control.
    /// </summary>
    public long HrtfWrites { get; private set; }

    /// <summary>
    /// Occurs when another client changes <c>target.object</c> or <c>target.node</c> of an attached stream. The voice has already forgotten the stream, whose node id is the second argument.
    /// </summary>
    public event Action<SpatialVoice, uint>? Released;

    /// <summary>
    /// Attaches a reported stream by its node id.
    /// </summary>
    /// <param name="stream">The stream to move onto the voice.</param>
    /// <returns><see langword="true"/> when the metadata was written. <see langword="false"/> when the session has no <c>default</c> metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The voice is disposed.</exception>
    public bool Attach(AudioStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Attach(stream.NodeId);
    }

    /// <summary>
    /// Sets <c>target.object</c> of a node to the capture node, so the session manager moves it onto the voice. The voice ignores metadata of the node until its own value comes back.
    /// </summary>
    /// <param name="streamNodeId">The node to move, for example a stream or a <see cref="SoundPlayer.NodeId"/>.</param>
    /// <returns><see langword="true"/> when the metadata was written. <see langword="false"/> when the session has no <c>default</c> metadata.</returns>
    /// <exception cref="ObjectDisposedException">The voice is disposed.</exception>
    public bool Attach(uint streamNodeId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session.DefaultMetadata is not { } metadata)
        {
            return false;
        }

        _streams.Add(streamNodeId);
        _awaitingEcho.Add(streamNodeId);
        metadata.SetProperty(streamNodeId, TargetObject, "Spa:Id", _serialText);
        return true;
    }

    /// <summary>
    /// Forgets an attached stream and clears its <c>target.object</c> when the node still exists, so the session manager moves it back.
    /// </summary>
    /// <param name="streamNodeId">The node to release.</param>
    /// <returns><see langword="true"/> when the stream was attached.</returns>
    public bool Detach(uint streamNodeId)
    {
        if (!_streams.Remove(streamNodeId))
        {
            return false;
        }

        _awaitingEcho.Remove(streamNodeId);

        if (_session.Globals.ContainsKey(streamNodeId))
        {
            _session.DefaultMetadata?.SetProperty(streamNodeId, TargetObject, null, null);
        }

        return true;
    }

    /// <summary>
    /// Forgets an attached stream without a change to its metadata. Use it when the stream has gone or another voice takes it over.
    /// </summary>
    /// <param name="streamNodeId">The node to forget.</param>
    public void Forget(uint streamNodeId)
    {
        _streams.Remove(streamNodeId);
        _awaitingEcho.Remove(streamNodeId);
    }

    /// <summary>
    /// Quantizes the parameters and sends only the controls that changed, in one param write. It allocates nothing. An HRTF change inside <see cref="MinHrtfInterval"/> waits for a timer.
    /// </summary>
    /// <param name="wanted">The new placement. A stereo voice uses only the wet level.</param>
    /// <exception cref="ObjectDisposedException">The voice is disposed.</exception>
    public void Update(in VoiceParams wanted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var q = Quantize(wanted);
        Current = q;
        if (Graph.Mode == VoiceMode.Stereo)
        {
            var level = Graph.Reverb ? Linear(q.WetDb) : 0;
            _wanted[WetLeft] = level;
            _wanted[WetRight] = level;
            if (Graph.Reverb)
            {
                Mark(WetLeft);
                Mark(WetRight);
            }

            Send();
            return;
        }

        _wanted[Freq] = q.CutoffHz;
        _wanted[Gain] = Linear(q.GainDb);
        var wet = Graph.Reverb ? Linear(q.WetDb) : 0;
        _wanted[WetLeft] = wet;
        _wanted[WetRight] = wet;
        if (Graph.Mode == VoiceMode.Hrtf)
        {
            _wanted[Azimuth] = q.Azimuth;
            _wanted[Elevation] = q.Elevation;
            _wanted[Radius] = q.Radius;
        }
        else
        {
            var lateral = MathF.Sin(q.Azimuth * MathF.PI / 180);
            var theta = (1 - lateral) * 0.25f * MathF.PI;
            _wanted[Left] = MathF.Round(MathF.Cos(theta), 4);
            _wanted[Right] = MathF.Round(MathF.Sin(theta), 4);
        }

        Mark(Freq);
        Mark(Gain);
        if (Graph.Reverb)
        {
            Mark(WetLeft);
            Mark(WetRight);
        }

        if (Graph.Mode == VoiceMode.Hrtf)
        {
            if (Differs(Azimuth) || Differs(Elevation) || Differs(Radius))
            {
                if (HrtfDue())
                {
                    MarkHrtf();
                }
                else
                {
                    _hrtfPending = true;
                    ArmTimer();
                }
            }
        }
        else
        {
            Mark(Left);
            Mark(Right);
        }

        Send();
    }

    /// <summary>
    /// Clears the target of each attached stream that still exists, destroys the node proxy, runs a roundtrip, and then unloads the module. Do not call it inside a PipeWire callback. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.MetadataChanged -= OnMetadataChanged;
        if (_session.DefaultMetadata is { } metadata)
        {
            foreach (var stream in _streams)
            {
                if (_session.Globals.ContainsKey(stream))
                {
                    metadata.SetProperty(stream, TargetObject, null, null);
                }
            }
        }

        _streams.Clear();
        _timer.Remove();
        _node.Dispose();
        _session.Roundtrip(500);
        _module.Dispose();
        _session.RemoveVoiceNodes(Name, Name + ".output");
        _builder.Dispose();
        BasinCounters.Untrack();
    }

    internal static VoiceParams Quantize(in VoiceParams wanted)
    {
        var azimuth = Step(wanted.Azimuth, AzimuthQuantum) % 360;
        if (azimuth < 0)
        {
            azimuth += 360;
        }

        var gain = wanted.GainDb <= VoiceParams.SilentDb ? VoiceParams.SilentDb : MathF.Min(20, Step(wanted.GainDb, GainQuantumDb));
        var wet = wanted.WetDb <= VoiceParams.SilentDb ? VoiceParams.SilentDb : MathF.Min(20, Step(wanted.WetDb, GainQuantumDb));
        var cutoff = Math.Clamp(wanted.CutoffHz, 20, VoiceParams.OpenCutoffHz);
        cutoff = VoiceParams.OpenCutoffHz * MathF.Pow(2, MathF.Min(0, Step(MathF.Log2(cutoff / VoiceParams.OpenCutoffHz), CutoffQuantumOctaves)));
        return new VoiceParams(
            azimuth,
            Math.Clamp(Step(wanted.Elevation, ElevationQuantum), -90, 90),
            MathF.Max(0, Step(wanted.Radius, RadiusQuantum)),
            gain,
            MathF.Round(cutoff, 1),
            wet);
    }

    private static float Step(float value, float quantum) => MathF.Round(value / quantum) * quantum;

    private static float Linear(float db) => db <= VoiceParams.SilentDb ? 0 : MathF.Pow(10, db / 20);

    private bool Differs(int control) => !(_sent[control] == _wanted[control]);

    private void Mark(int control) => _dirty[control] |= Differs(control);

    private void MarkHrtf()
    {
        var clamped = Math.Clamp(_wanted[Radius], Graph.HrtfRadiusMin, Graph.HrtfRadiusMax);
        _wanted[Radius] = clamped;
        Mark(Azimuth);
        Mark(Elevation);
        Mark(Radius);
        if (_dirty[Azimuth] || _dirty[Elevation] || _dirty[Radius])
        {
            _lastHrtf = Stopwatch.GetTimestamp();
            HrtfWrites++;
        }

        _hrtfPending = false;
    }

    private bool HrtfDue() =>
        Stopwatch.GetElapsedTime(_lastHrtf) >= MinHrtfInterval;

    private void ArmTimer()
    {
        if (_timerArmed)
        {
            return;
        }

        var remaining = MinHrtfInterval - Stopwatch.GetElapsedTime(_lastHrtf);
        _timer.UpdateTimer(Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds)));
        _timerArmed = true;
    }

    private void OnHrtfTimer()
    {
        _timerArmed = false;
        if (_disposed || !_hrtfPending)
        {
            return;
        }

        MarkHrtf();
        Send();
    }

    private void Send()
    {
        var any = false;
        for (var i = 0; i < _dirty.Length; i++)
        {
            any |= _dirty[i];
        }

        if (!any)
        {
            return;
        }

        _builder.Reset();
        using (_builder.PushObject(SPA_TYPE_OBJECT_Props, (uint)spa_param_type.SPA_PARAM_Props))
        {
            _builder.AddProperty((uint)spa_prop.SPA_PROP_params);
            using (_builder.PushStruct())
            {
                for (var i = 0; i < _dirty.Length; i++)
                {
                    if (!_dirty[i])
                    {
                        continue;
                    }

                    _builder.AddString(ControlNames[i]);
                    _builder.AddFloat(_wanted[i]);
                    _sent[i] = _wanted[i];
                    _dirty[i] = false;
                }
            }
        }

        if (!SpaInterface.TryGetMethods((spa_interface*)_node.Handle, PW_VERSION_NODE_METHODS, out pw_node_methods* methods, out var data) ||
            methods->set_param is null)
        {
            return;
        }

        fixed (byte* pod = _builder.WrittenSpan)
        {
            var result = methods->set_param(data, (uint)spa_param_type.SPA_PARAM_Props, 0, (spa_pod*)pod);
            if (result < 0 && !_paramFailureLogged)
            {
                _paramFailureLogged = true;
                Log.Warn($"voice {Name}: set_param failed ({result}), the voice keeps its previous controls");
            }
        }

        ParamWrites++;
    }

    private void OnMetadataChanged(PipeWireMetadataProperty property)
    {
        if (!_streams.Contains(property.Subject))
        {
            return;
        }

        if (_awaitingEcho.Contains(property.Subject))
        {
            if (property.Key == TargetObject && property.Value == _serialText)
            {
                _awaitingEcho.Remove(property.Subject);
            }

            return;
        }

        var ours = property.Key switch
        {
            TargetObject => property.Value == _serialText,
            TargetNode => property.Value is null || property.Value == _nodeText,
            null => true,
            _ => true,
        };
        if (ours)
        {
            return;
        }

        _streams.Remove(property.Subject);
        _awaitingEcho.Remove(property.Subject);
        Released?.Invoke(this, property.Subject);
    }
}
