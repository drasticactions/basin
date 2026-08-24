using Basin.Plasma.Protocol;

namespace Basin.Plasma;

public sealed class ExternalBrightnessDevice
{
    private const int CoalesceMillis = 100;

    private readonly ExternalBrightnessManager _owner;
    private readonly KdeExternalBrightnessDeviceV1Resource _resource;
    private readonly ICompositorEventLoop _loop;
    private bool _pendingInternal;
    private byte[] _pendingEdid = [];
    private uint _pendingMax;
    private uint? _pendingObserved;
    private bool _pendingDdcCi;
    private bool _committed;
    private IEventSource? _coalesceTimer;
    private uint? _coalesced;

    internal ExternalBrightnessDevice(
        ExternalBrightnessManager owner, KdeExternalBrightnessDeviceV1Resource resource, ICompositorEventLoop loop)
    {
        _owner = owner;
        _resource = resource;
        _loop = loop;
        resource.SetInternal += (_, e) => _pendingInternal = e.Internal == 1;
        resource.SetEdid += (_, e) => _pendingEdid = DecodeEdid(e.String);
        resource.SetMaxBrightness += (_, e) => _pendingMax = e.Value;
        resource.SetObservedBrightness += (_, e) => _pendingObserved = e.Value;
        resource.SetUsesDdcCi += (_, e) => _pendingDdcCi = e.Value == 1;
        resource.Commit += (_, _) => OnCommit();
        resource.Destroyed += (_, _) =>
        {
            _coalesceTimer?.Remove();
            _coalesceTimer = null;
            _owner.Unregister(this);
        };
    }

    public bool IsInternal { get; private set; }

    public uint MaxBrightness { get; private set; }

    public uint? ObservedBrightness { get; private set; }

    public bool UsesDdcCi { get; private set; }

    public uint? RequestedBrightness { get; private set; }

    public IOutput? Output { get; internal set; }

    internal ReadOnlyMemory<byte> EdidBlock { get; private set; } = ReadOnlyMemory<byte>.Empty;

    internal void Request(uint value)
    {
        RequestedBrightness = value;
        if (!UsesDdcCi)
        {
            Send(value);
            return;
        }

        if (_coalesceTimer is null)
        {
            _coalesced = value;
            _coalesceTimer = _loop.AddTimer(OnCoalesceTimer);
            _coalesceTimer.UpdateTimer(CoalesceMillis);
            return;
        }

        _coalesced = value;
    }

    private void OnCoalesceTimer()
    {
        if (_coalesced is { } value)
        {
            _coalesced = null;
            Send(value);
            _coalesceTimer?.UpdateTimer(CoalesceMillis);
            return;
        }

        _coalesceTimer?.Remove();
        _coalesceTimer = null;
    }

    private void Send(uint value)
    {
        if (!_resource.IsDestroyed)
        {
            _resource.SendRequestedBrightness(value);
        }
    }

    private void OnCommit()
    {
        IsInternal = _pendingInternal;
        MaxBrightness = _pendingMax;
        UsesDdcCi = _pendingDdcCi;
        EdidBlock = _pendingEdid;
        var observedChanged = _pendingObserved is { } observed && observed != ObservedBrightness;
        ObservedBrightness = _pendingObserved;
        var first = !_committed;
        _committed = true;
        _owner.OnDeviceCommitted(this, first, observedChanged);
    }

    private static byte[] DecodeEdid(string encoded)
    {
        if (encoded.Length == 0)
        {
            return [];
        }

        try
        {
            var bytes = Convert.FromBase64String(encoded);
            return bytes.Length >= 128 ? bytes : [];
        }
        catch (FormatException)
        {
            return [];
        }
    }
}
