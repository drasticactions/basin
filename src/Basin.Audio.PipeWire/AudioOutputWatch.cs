using System.Globalization;
using System.Text.Json;
using Basin.Diagnostics;
using PipeWire;
using PipeWire.Native;
using PipeWire.Spa;

namespace Basin.Audio.PipeWire;

/// <summary>
/// Follows <c>default.audio.sink</c> and tells whether that sink plays through headphones or speakers. It reads the form factor of the node and its device, and the active output route.
/// </summary>
public sealed class AudioOutputWatch : IDisposable
{
    private const string DefaultSinkKey = "default.audio.sink";

    private static readonly string[] HeadphoneForms = ["headphone", "headphones", "headset", "hands-free", "handset"];

    private readonly AudioSession _session;
    private readonly Dictionary<int, (string? Name, string? PortType)> _routes = [];
    private PipeWireNode? _node;
    private PipeWireDevice? _device;
    private uint _nodeId = uint.MaxValue;
    private uint _deviceId = uint.MaxValue;
    private int _routeDevice = -1;
    private string? _nodeForm;
    private string? _deviceForm;
    private bool _nodeSeen;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioOutputWatch"/> class. It reads the current default sink at once when the session has seen it.
    /// </summary>
    /// <param name="session">The session to watch.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public AudioOutputWatch(AudioSession session)
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
    /// Gets the name of the active output route of the sink device, or <see langword="null"/> when there is none.
    /// </summary>
    public string? RouteName { get; private set; }

    /// <summary>
    /// Gets the <c>port.type</c> of the active output route, for example <c>headphones</c>, or <see langword="null"/>.
    /// </summary>
    public string? PortType { get; private set; }

    /// <summary>
    /// Gets the <c>device.form-factor</c> of the sink node, else of its device, or <see langword="null"/>.
    /// </summary>
    public string? FormFactor => _nodeForm ?? _deviceForm;

    /// <summary>
    /// Gets the classification of the default sink. It is <see cref="AudioOutputKind.Unknown"/> while no default sink is set or its node has not sent properties. Otherwise it is never Unknown.
    /// </summary>
    public AudioOutputKind Kind { get; private set; }

    /// <summary>
    /// Occurs when <see cref="Kind"/>, <see cref="RouteName"/> or <see cref="PortType"/> changes. A change of <see cref="SinkName"/> alone does not raise it.
    /// </summary>
    public event Action<AudioOutputWatch>? Changed;

    /// <summary>
    /// Classifies an output from its properties. Headphones when the form factor is headphone, headphones, headset, hands-free or handset, when the port type is headphones, headset or handsfree, or when the route name contains headphone or headset.
    /// </summary>
    /// <param name="formFactor">The <c>device.form-factor</c>, or <see langword="null"/>.</param>
    /// <param name="portType">The <c>port.type</c> of the route, or <see langword="null"/>.</param>
    /// <param name="routeName">The route name, compared without case, or <see langword="null"/>.</param>
    /// <returns><see cref="AudioOutputKind.Headphones"/> on a match. <see cref="AudioOutputKind.Unknown"/> when all three are <see langword="null"/>. Else <see cref="AudioOutputKind.Speakers"/>.</returns>
    public static AudioOutputKind Classify(string? formFactor, string? portType, string? routeName)
    {
        if (formFactor is { Length: > 0 } form && Array.IndexOf(HeadphoneForms, form) >= 0)
        {
            return AudioOutputKind.Headphones;
        }

        if (portType is "headphones" or "headset" or "handsfree")
        {
            return AudioOutputKind.Headphones;
        }

        if (routeName is { Length: > 0 } route &&
            (route.Contains("headphone", StringComparison.OrdinalIgnoreCase) || route.Contains("headset", StringComparison.OrdinalIgnoreCase)))
        {
            return AudioOutputKind.Headphones;
        }

        return formFactor is null && portType is null && routeName is null ? AudioOutputKind.Unknown : AudioOutputKind.Speakers;
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
        BasinCounters.Untrack();
    }

    internal static string? SinkNameOf(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void OnMetadataChanged(PipeWireMetadataProperty property)
    {
        if (property.Subject != 0 || property.Key != DefaultSinkKey)
        {
            return;
        }

        var name = SinkNameOf(property.Value);
        if (name == SinkName)
        {
            return;
        }

        ReleaseSink();
        SinkName = name;
        if (name is not null && _session.FindNode(name) is { } global)
        {
            BindSink(global);
            return;
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
            _device?.Dispose();
            _device = null;
            _deviceForm = null;
            _routes.Clear();
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

            _nodeSeen = true;
            _nodeForm = info.Properties.GetValueOrDefault("device.form-factor");
            _routeDevice = int.TryParse(info.Properties.GetValueOrDefault("card.profile.device"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var device) ? device : -1;
            if (uint.TryParse(info.Properties.GetValueOrDefault("device.id"), NumberStyles.None, CultureInfo.InvariantCulture, out var deviceId) && deviceId != _deviceId)
            {
                _device?.Dispose();
                _device = null;
                _routes.Clear();
                _deviceId = deviceId;
                if (_session.Globals.ContainsKey(deviceId))
                {
                    BindDevice(deviceId);
                }
            }

            Recompute();
        };
    }

    private void BindDevice(uint deviceId)
    {
        _device = _session.Registry.BindDevice(deviceId);
        _device.Info += (_, info) =>
        {
            if (info.Properties.Count == 0)
            {
                return;
            }

            _deviceForm = info.Properties.GetValueOrDefault("device.form-factor");
            Recompute();
        };
        _device.ParamChanged += (_, e) =>
        {
            if (e.ParamType == spa_param_type.SPA_PARAM_Route && ReadRoute(e.Param, out var device, out var name, out var port))
            {
                _routes[device] = (name, port);
                Recompute();
            }
        };
        _device.SubscribeParams(spa_param_type.SPA_PARAM_Route);
    }

    private void ReleaseSink()
    {
        _node?.Dispose();
        _node = null;
        _device?.Dispose();
        _device = null;
        _nodeId = uint.MaxValue;
        _deviceId = uint.MaxValue;
        _routeDevice = -1;
        _nodeSeen = false;
        _nodeForm = null;
        _deviceForm = null;
        _routes.Clear();
    }

    private void Recompute()
    {
        (string? Name, string? PortType) route = default;
        if (_routeDevice >= 0)
        {
            _routes.TryGetValue(_routeDevice, out route);
        }
        else if (_routes.Count == 1)
        {
            foreach (var only in _routes.Values)
            {
                route = only;
            }
        }

        var kind = SinkName is null || !_nodeSeen ? AudioOutputKind.Unknown : Classify(FormFactor, route.PortType, route.Name);
        if (kind == AudioOutputKind.Unknown && SinkName is not null && _nodeSeen)
        {
            kind = AudioOutputKind.Speakers;
        }

        if (kind == Kind && route.Name == RouteName && route.PortType == PortType)
        {
            return;
        }

        Kind = kind;
        RouteName = route.Name;
        PortType = route.PortType;
        Changed?.Invoke(this);
    }

    private static bool ReadRoute(SpaPod pod, out int device, out string? name, out string? portType)
    {
        device = -1;
        name = portType = null;
        if (pod.IsNull || pod.Type != Pipewire.SPA_TYPE_Object)
        {
            return false;
        }

        var route = pod.AsObject();
        if (route.TryGetValue((uint)spa_param_route.SPA_PARAM_ROUTE_direction, out var direction) &&
            direction.TryGetId(out var dir) && dir != (uint)spa_direction.SPA_DIRECTION_OUTPUT)
        {
            return false;
        }

        if (route.TryGetValue((uint)spa_param_route.SPA_PARAM_ROUTE_device, out var index))
        {
            _ = index.TryGetInt(out device);
        }

        if (route.TryGetValue((uint)spa_param_route.SPA_PARAM_ROUTE_name, out var text))
        {
            _ = text.TryGetString(out name);
        }

        if (route.TryGetValue((uint)spa_param_route.SPA_PARAM_ROUTE_info, out var info) && info.Type == Pipewire.SPA_TYPE_Struct)
        {
            string? key = null;
            var first = true;
            foreach (var item in info.AsStruct())
            {
                if (first)
                {
                    first = false;
                    continue;
                }

                if (!item.TryGetString(out var value))
                {
                    continue;
                }

                if (key is null)
                {
                    key = value;
                    continue;
                }

                if (key == "port.type")
                {
                    portType = value;
                }

                key = null;
            }
        }

        return device >= 0 || name is not null;
    }
}
