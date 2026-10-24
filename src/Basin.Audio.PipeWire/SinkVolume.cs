using System.Globalization;
using Basin.Diagnostics;
using PipeWire;
using PipeWire.Native;
using PipeWire.Spa;
using static PipeWire.Native.Pipewire;

namespace Basin.Audio.PipeWire;

/// <summary>
/// Reads and writes the volume and mute of the default sink. It writes through the output route of the sink device when that route has channel volumes, else through the node props.
/// </summary>
public sealed class SinkVolume : IDisposable
{
    /// <summary>
    /// The largest cubic volume that <see cref="SetVolume"/> writes.
    /// </summary>
    public const float MaxVolume = 1.5f;

    private const string DefaultSinkKey = "default.audio.sink";
    private const int DefaultChannels = 2;

    private readonly AudioSession _session;
    private readonly SpaPodBuilder _builder = new(512);
    private float[] _nodeVolumes = [];
    private float[] _routeVolumes = [];
    private PipeWireNode? _node;
    private PipeWireDevice? _device;
    private uint _nodeId = uint.MaxValue;
    private uint _deviceId = uint.MaxValue;
    private int _routeDevice = -1;
    private int _routeIndex = -1;
    private int _channels = DefaultChannels;
    private string? _reportedSink;
    private bool _nodeMuted;
    private bool _routeMuted;
    private bool _nodeRead;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SinkVolume"/> class. It binds the current default sink at once when the session has seen it.
    /// </summary>
    /// <param name="session">The session to watch and write through.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public SinkVolume(AudioSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _session.MetadataChanged += OnMetadataChanged;
        _session.GlobalAdded += OnGlobalAdded;
        _session.GlobalRemoved += OnGlobalRemoved;
        BasinCounters.Track();
        if (session.DefaultProperty(DefaultSinkKey) is { } current)
        {
            OnMetadataChanged(new PipeWireMetadataProperty(0, DefaultSinkKey, null, current));
        }
    }

    /// <summary>
    /// Gets the <c>node.name</c> of the default sink, or <see langword="null"/> when no default sink is set.
    /// </summary>
    public string? SinkName { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a default sink is bound and its volume is read. The setters return false while it is false.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the volume goes through the output route of the device instead of the node props.
    /// </summary>
    public bool UsesRoute { get; private set; }

    /// <summary>
    /// Gets the cubic volume that wpctl and pavucontrol show. It is the cube root of <see cref="LinearVolume"/>, and 0 while not available.
    /// </summary>
    public float Volume { get; private set; }

    /// <summary>
    /// Gets the average linear gain of the channels, as the daemon holds it, or 0 while not available.
    /// </summary>
    public float LinearVolume { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the sink is muted, as the daemon holds it. It is false while not available.
    /// </summary>
    public bool Muted { get; private set; }

    /// <summary>
    /// Occurs when the daemon reports a new volume or mute, when the path changes, or when the default sink changes or goes away.
    /// </summary>
    public event Action<SinkVolume>? Changed;

    /// <summary>
    /// Converts a linear gain to the cubic volume scale.
    /// </summary>
    /// <param name="linear">The linear gain.</param>
    /// <returns>The cube root of <paramref name="linear"/>, or 0 when it is 0 or less.</returns>
    public static float ToCubic(float linear) => linear <= 0 ? 0 : MathF.Cbrt(linear);

    /// <summary>
    /// Converts a cubic volume to a linear gain.
    /// </summary>
    /// <param name="cubic">The cubic volume.</param>
    /// <returns>The cube of <paramref name="cubic"/>, or 0 when it is 0 or less.</returns>
    public static float ToLinear(float cubic) => cubic <= 0 ? 0 : cubic * cubic * cubic;

    /// <summary>
    /// Writes the same gain to every channel, which removes a balance between them. The properties do not change until the daemon reports the new value.
    /// </summary>
    /// <param name="volume">The cubic volume, clamped to 0 through <see cref="MaxVolume"/>.</param>
    /// <returns><see langword="true"/> when the write was sent. <see langword="false"/> when the object is disposed, not available, or <paramref name="volume"/> is NaN.</returns>
    public bool SetVolume(float volume)
    {
        if (_disposed || !IsAvailable || _node is null || float.IsNaN(volume))
        {
            return false;
        }

        var linear = ToLinear(Math.Clamp(volume, 0, MaxVolume));
        Span<float> volumes = stackalloc float[Math.Clamp(_channels, 1, 64)];
        volumes.Fill(linear);
        return Apply(volumes, mute: null);
    }

    /// <summary>
    /// Writes only the mute, so the level stays. The properties do not change until the daemon reports the new value.
    /// </summary>
    /// <param name="muted"><see langword="true"/> to mute the sink.</param>
    /// <returns><see langword="true"/> when the write was sent. <see langword="false"/> when the object is disposed or not available.</returns>
    public bool SetMuted(bool muted)
    {
        if (_disposed || !IsAvailable || _node is null)
        {
            return false;
        }

        return Apply([], muted);
    }

    /// <summary>
    /// Stops following the session and destroys the sink and device proxies. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.MetadataChanged -= OnMetadataChanged;
        _session.GlobalAdded -= OnGlobalAdded;
        _session.GlobalRemoved -= OnGlobalRemoved;
        ReleaseSink();
        _builder.Dispose();
        BasinCounters.Untrack();
    }

    internal static void BuildProps(SpaPodBuilder builder, ReadOnlySpan<float> volumes, bool? mute)
    {
        using (builder.PushObject(SPA_TYPE_OBJECT_Props, (uint)spa_param_type.SPA_PARAM_Props))
        {
            if (volumes.Length > 0)
            {
                builder.AddProperty((uint)spa_prop.SPA_PROP_channelVolumes);
                builder.AddArray(SPA_TYPE_Float, sizeof(float), volumes);
            }

            if (mute is { } muted)
            {
                builder.AddProperty((uint)spa_prop.SPA_PROP_mute);
                builder.AddBool(muted);
            }
        }
    }

    internal static void BuildRoute(SpaPodBuilder builder, int index, int device, ReadOnlySpan<float> volumes, bool? mute)
    {
        using (builder.PushObject(SPA_TYPE_OBJECT_ParamRoute, (uint)spa_param_type.SPA_PARAM_Route))
        {
            builder.AddProperty((uint)spa_param_route.SPA_PARAM_ROUTE_index);
            builder.AddInt(index);
            builder.AddProperty((uint)spa_param_route.SPA_PARAM_ROUTE_device);
            builder.AddInt(device);
            builder.AddProperty((uint)spa_param_route.SPA_PARAM_ROUTE_props);
            BuildProps(builder, volumes, mute);
            builder.AddProperty((uint)spa_param_route.SPA_PARAM_ROUTE_save);
            builder.AddBool(true);
        }
    }

    internal static bool ReadProps(SpaPod pod, ref float[] volumes, ref bool muted)
    {
        if (pod.IsNull || pod.Type != SPA_TYPE_Object)
        {
            return false;
        }

        var props = pod.AsObject();
        var read = false;
        if (props.TryGetValue((uint)spa_prop.SPA_PROP_channelVolumes, out var channels) && channels.Type == SPA_TYPE_Array)
        {
            var array = channels.AsArray();
            if (array.ChildType == SPA_TYPE_Float && array.ChildSize == sizeof(float))
            {
                volumes = array.AsSpan<float>().ToArray();
                read = true;
            }
        }

        if (props.TryGetValue((uint)spa_prop.SPA_PROP_mute, out var mute) && mute.TryGetBool(out var value))
        {
            muted = value;
            read = true;
        }

        return read;
    }

    internal static bool ReadRoute(SpaPod pod, out int index, out int device, out SpaPod props)
    {
        index = device = -1;
        props = default;
        if (pod.IsNull || pod.Type != SPA_TYPE_Object)
        {
            return false;
        }

        var route = pod.AsObject();
        if (route.TryGetValue((uint)spa_param_route.SPA_PARAM_ROUTE_direction, out var direction) &&
            direction.TryGetId(out var dir) && dir != (uint)spa_direction.SPA_DIRECTION_OUTPUT)
        {
            return false;
        }

        if (!route.TryGetValue((uint)spa_param_route.SPA_PARAM_ROUTE_index, out var indexPod) || !indexPod.TryGetInt(out index) ||
            !route.TryGetValue((uint)spa_param_route.SPA_PARAM_ROUTE_device, out var devicePod) || !devicePod.TryGetInt(out device))
        {
            return false;
        }

        _ = route.TryGetValue((uint)spa_param_route.SPA_PARAM_ROUTE_props, out props);
        return true;
    }

    internal static float Average(ReadOnlySpan<float> volumes)
    {
        if (volumes.Length == 0)
        {
            return 0;
        }

        var sum = 0f;
        foreach (var volume in volumes)
        {
            sum += volume;
        }

        return sum / volumes.Length;
    }

    private bool Apply(ReadOnlySpan<float> volumes, bool? mute)
    {
        _builder.Reset();
        if (UsesRoute && _device is { } device)
        {
            BuildRoute(_builder, _routeIndex, _routeDevice, volumes, mute);
            device.SetParam(spa_param_type.SPA_PARAM_Route, _builder.WrittenSpan);
        }
        else
        {
            BuildProps(_builder, volumes, mute);
            _node!.SetParam(spa_param_type.SPA_PARAM_Props, _builder.WrittenSpan);
        }

        return true;
    }

    private void OnMetadataChanged(PipeWireMetadataProperty property)
    {
        if (property.Subject != 0 || (property.Key is not null && property.Key != DefaultSinkKey))
        {
            return;
        }

        var name = property.Key is null ? null : AudioOutputWatch.SinkNameOf(property.Value);
        if (name == SinkName)
        {
            return;
        }

        ReleaseSink();
        SinkName = name;
        if (name is not null && _session.FindNode(name) is { } global)
        {
            BindSink(global);
        }

        Recompute();
    }

    private void OnGlobalAdded(PipeWireGlobal global)
    {
        if (_node is null && SinkName is { } name && global.IsNode && global.GetProperty("node.name") == name)
        {
            BindSink(global);
        }
        else if (_device is null && global.IsDevice && global.Id == _deviceId)
        {
            BindDevice(global.Id);
        }
    }

    private void OnGlobalRemoved(uint id)
    {
        if (id == _nodeId)
        {
            ReleaseSink();
            Recompute();
        }
        else if (id == _deviceId)
        {
            ReleaseDevice();
            Recompute();
        }
    }

    private void BindSink(PipeWireGlobal global)
    {
        _nodeId = global.Id;
        _node = _session.Registry.BindNode(global.Id);
        _node.Info += (_, info) =>
        {
            if (info.Properties.Count == 0)
            {
                return;
            }

            if (int.TryParse(info.Properties.GetValueOrDefault("audio.channels"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var channels) && channels > 0 && _nodeVolumes.Length == 0)
            {
                _channels = channels;
            }

            _routeDevice = int.TryParse(info.Properties.GetValueOrDefault("card.profile.device"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var routeDevice) ? routeDevice : -1;
            if (uint.TryParse(info.Properties.GetValueOrDefault("device.id"), NumberStyles.None, CultureInfo.InvariantCulture, out var deviceId))
            {
                if (deviceId != _deviceId)
                {
                    ReleaseDevice();
                    _deviceId = deviceId;
                    if (_routeDevice >= 0 && _session.Globals.ContainsKey(deviceId))
                    {
                        BindDevice(deviceId);
                    }
                }
            }
            else if (_deviceId != uint.MaxValue)
            {
                ReleaseDevice();
                _deviceId = uint.MaxValue;
            }

            Recompute();
        };
        _node.ParamChanged += (_, e) =>
        {
            if (e.ParamType == spa_param_type.SPA_PARAM_Props && ReadProps(e.Param, ref _nodeVolumes, ref _nodeMuted))
            {
                _nodeRead = true;
                if (_nodeVolumes.Length > 0)
                {
                    _channels = _nodeVolumes.Length;
                }

                Recompute();
            }
        };
        _node.SubscribeParams(spa_param_type.SPA_PARAM_Props);
    }

    private void BindDevice(uint deviceId)
    {
        _device = _session.Registry.BindDevice(deviceId);
        _device.ParamChanged += (_, e) =>
        {
            if (e.ParamType != spa_param_type.SPA_PARAM_Route || !ReadRoute(e.Param, out var index, out var device, out var props) || device != _routeDevice)
            {
                return;
            }

            _routeIndex = index;
            _ = ReadProps(props, ref _routeVolumes, ref _routeMuted);
            if (_routeVolumes.Length > 0)
            {
                _channels = _routeVolumes.Length;
            }

            Recompute();
        };
        _device.SubscribeParams(spa_param_type.SPA_PARAM_Route);
    }

    private void ReleaseDevice()
    {
        _device?.Dispose();
        _device = null;
        _routeIndex = -1;
        _routeVolumes = [];
        _routeMuted = false;
    }

    private void ReleaseSink()
    {
        _node?.Dispose();
        _node = null;
        ReleaseDevice();
        _nodeId = uint.MaxValue;
        _deviceId = uint.MaxValue;
        _routeDevice = -1;
        _nodeVolumes = [];
        _nodeMuted = false;
        _nodeRead = false;
        _channels = DefaultChannels;
    }

    private void Recompute()
    {
        var usesRoute = _device is not null && _routeIndex >= 0 && _routeVolumes.Length > 0;
        var volumes = usesRoute ? _routeVolumes : _nodeVolumes;
        var available = _node is not null && (usesRoute || _nodeRead);
        var linear = available ? Average(volumes) : 0;
        var muted = available && (usesRoute ? _routeMuted : _nodeMuted);
        var cubic = ToCubic(linear);
        if (available == IsAvailable && usesRoute == UsesRoute && linear == LinearVolume && muted == Muted && SinkName == _reportedSink)
        {
            return;
        }

        _reportedSink = SinkName;

        IsAvailable = available;
        UsesRoute = usesRoute;
        LinearVolume = linear;
        Volume = cubic;
        Muted = muted;
        Changed?.Invoke(this);
    }
}
