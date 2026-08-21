using Basin.Diagnostics;
using Wayland;
using Wayland.Server;

namespace Basin.Seat;

public sealed class Seat : IDisposable
{
    public const int Version = 11;

    private const int SerialHistory = 128;

    private readonly WlGlobal _global;
    private readonly List<SeatClient> _clients = [];
    private readonly (uint Serial, SerialKind Kind)[] _serials = new (uint, SerialKind)[SerialHistory];
    private readonly List<WlSeatResource> _resources = [];
    private int _serialCount;
    private SeatCapability _capabilities;
    private bool _disposed;

    public Seat(WlServerDisplay display, CompositorGlobal compositor, string name = "seat0", SeatCapability capabilities = SeatCapability.Pointer | SeatCapability.Keyboard)
    {
        Display = display;
        Compositor = compositor;
        Name = name;
        _capabilities = capabilities;
        Pointer = new SeatPointer(this);
        Keyboard = new SeatKeyboard(this);
        Touch = new SeatTouch(this);
        DataDevice = new SeatDataDevice(this);
        BasinCounters.Track();

        _global = display.CreateGlobal(WlSeat.Interface, Version, OnBind);
    }

    public WlServerDisplay Display { get; }

    public CompositorGlobal Compositor { get; }

    public string Name { get; }

    public uint NameFor(WlClient client) => _global.NameFor(client);

    internal Surface? ResolveSurface(Wayland.WlSurfaceResource? resource) => Compositor.ResolveSurface(resource);

    public SeatPointer Pointer { get; }

    public SeatKeyboard Keyboard { get; }

    public SeatTouch Touch { get; }

    public SeatDataDevice DataDevice { get; }

    public void SetCapability(SeatCapability capability, bool present) =>
        Capabilities = present ? Capabilities | capability : Capabilities & ~capability;

    public SeatCapability Capabilities
    {
        get => _capabilities;
        set
        {
            if (_capabilities == value)
            {
                return;
            }

            _capabilities = value;
            foreach (var resource in _resources)
            {
                resource.SendCapabilities((WlSeat.Capability)value);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Keyboard.Dispose();
        _global.Dispose();
        BasinCounters.Untrack();
    }

    public void Retire(int graceMillis = GlobalRetirement.DefaultGraceMillis) =>
        GlobalRetirement.Retire(Display, _global, Dispose, graceMillis);

    public uint NextSerial(SerialKind kind)
    {
        var serial = Display.NextSerial();
        _serials[_serialCount++ % SerialHistory] = (serial, kind);
        return serial;
    }

    public bool TryGetSerial(uint serial, out SerialKind kind)
    {
        var count = Math.Min(_serialCount, SerialHistory);
        for (var i = 0; i < count; i++)
        {
            if (_serials[i].Serial == serial)
            {
                kind = _serials[i].Kind;
                return true;
            }
        }

        kind = SerialKind.Other;
        return false;
    }

    public bool ValidateGrabSerial(uint serial) =>
        TryGetSerial(serial, out var kind) &&
        kind is SerialKind.PointerButtonPress or SerialKind.KeyPress or SerialKind.TouchDown;

    public bool ValidateSelectionSerial(uint serial) => TryGetSerial(serial, out _);

    public bool ValidateImplicitGrabSerial(uint serial) =>
        TryGetSerial(serial, out var kind) && kind switch
        {
            SerialKind.PointerButtonPress => Pointer.HasImplicitGrab && Pointer.GrabSerial == serial,
            SerialKind.TouchDown => Touch.IsActiveDownSerial(serial),
            SerialKind.KeyPress => true,
            _ => false,
        };

    internal IReadOnlyList<SeatClient> Clients => _clients;

    internal bool OwnsResource(WlSeatResource resource) => _resources.Contains(resource);

    internal SeatClient ClientFor(WlClient client)
    {
        foreach (var candidate in _clients)
        {
            if (candidate.Client == client)
            {
                return candidate;
            }
        }

        var created = new SeatClient(this, client);
        _clients.Add(created);
        return created;
    }

    internal SeatClient? ClientOf(Surface? surface) =>
        surface is null || surface.IsDestroyed ? null : ClientFor(surface.Resource.Client);

    internal void PruneClient(SeatClient client)
    {
        if (client.IsEmpty)
        {
            _clients.Remove(client);
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new WlSeatResource(client, version, id);
        _resources.Add(resource);
        resource.Destroyed += (_, _) => _resources.Remove(resource);
        resource.SendCapabilities((WlSeat.Capability)_capabilities);
        if (version >= 2)
        {
            resource.SendName(Name);
        }

        var seatClient = ClientFor(client);
        resource.GetPointer += (_, e) => seatClient.AddPointer(new WlPointerResource(client, resource.Version, e.Id));
        resource.GetKeyboard += (_, e) => seatClient.AddKeyboard(new WlKeyboardResource(client, resource.Version, e.Id));
        resource.GetTouch += (_, e) => seatClient.AddTouch(new WlTouchResource(client, resource.Version, e.Id));
    }
}
