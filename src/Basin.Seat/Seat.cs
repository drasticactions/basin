using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;
using Wayland.Server;

namespace Basin.Seat;

/// <summary>
/// One <c>wl_seat</c> global: the delivery mechanism for one user's pointer, keyboard, touch and selections. The consumer decides focus and calls the notify methods.
/// </summary>
public sealed class Seat : IDisposable
{
    /// <summary>
    /// The <c>wl_seat</c> version that the global advertises.
    /// </summary>
    public const int Version = 11;

    private const int SerialHistory = 128;

    private static readonly Dictionary<WlSeatResource, Seat> ByResource = [];

    private readonly WlGlobal _global;
    private readonly List<SeatClient> _clients = [];
    private readonly (uint Serial, SerialKind Kind)[] _serials = new (uint, SerialKind)[SerialHistory];
    private readonly List<WlSeatResource> _resources = [];
    private readonly int[] _virtualDevices = new int[3];
    private SeatCapability _virtualOwned;
    private int _serialCount;
    private SeatCapability _capabilities;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Seat"/> class and creates its <c>wl_seat</c> global.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="compositor">The compositor global that resolves surface resources.</param>
    /// <param name="name">The seat name sent to clients at version 2 and later.</param>
    /// <param name="capabilities">The capabilities that the seat advertises at the start.</param>
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

    /// <summary>
    /// Gets the display that hosts the global and issues the serials.
    /// </summary>
    public WlServerDisplay Display { get; }

    /// <summary>
    /// Gets the compositor global that resolves <c>wl_surface</c> resources for this seat.
    /// </summary>
    public CompositorGlobal Compositor { get; }

    /// <summary>
    /// Gets the seat name that clients receive in <c>wl_seat.name</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the registry name of the seat global as one client sees it.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <returns>The numeric name in that client's registry.</returns>
    public uint NameFor(WlClient client) => _global.NameFor(client);

    /// <summary>
    /// Finds the seat that a <c>wl_seat</c> resource was bound from.
    /// </summary>
    /// <param name="resource">The resource, or <see langword="null"/>.</param>
    /// <returns>The seat, or <see langword="null"/> when the resource is null, destroyed or from a disposed seat.</returns>
    public static Seat? FromResource(WlSeatResource? resource) =>
        resource is not null && ByResource.TryGetValue(resource, out var seat) ? seat : null;

    /// <summary>
    /// Gets or sets the sink that virtual pointer and virtual keyboard clients inject into for this seat. When it is null, those managers use their own default.
    /// </summary>
    public IInputSink? InputSink { get; set; }

    /// <summary>
    /// Gets or sets the target that <see cref="SeatSyntheticInput"/> drives for this seat, usually the backend's input injector.
    /// </summary>
    public ISyntheticInput? SyntheticInput { get; set; }

    internal Surface? ResolveSurface(Wayland.WlSurfaceResource? resource) => Compositor.ResolveSurface(resource);

    /// <summary>
    /// Gets the pointer device. It exists whether or not the pointer capability is advertised.
    /// </summary>
    public SeatPointer Pointer { get; }

    /// <summary>
    /// Gets the keyboard device. It exists whether or not the keyboard capability is advertised.
    /// </summary>
    public SeatKeyboard Keyboard { get; }

    /// <summary>
    /// Gets or sets the monitor that sees each key before the compositor's bindings and the client, or <see langword="null"/> for none.
    /// The seat backends consult it for every key source.
    /// </summary>
    public IKeyMonitor? KeyMonitor { get; set; }

    /// <summary>
    /// Gets the touch device. It exists whether or not the touch capability is advertised.
    /// </summary>
    public SeatTouch Touch { get; }

    /// <summary>
    /// Gets the data device, which holds the selections and runs drag-and-drop.
    /// </summary>
    public SeatDataDevice DataDevice { get; }

    /// <summary>
    /// Adds or removes capabilities. A change sends <c>wl_seat.capabilities</c> to every bound resource.
    /// </summary>
    /// <param name="capability">The capabilities to change.</param>
    /// <param name="present"><see langword="true"/> to add them, <see langword="false"/> to remove them.</param>
    public void SetCapability(SeatCapability capability, bool present) =>
        Capabilities = present ? Capabilities | capability : Capabilities & ~capability;

    /// <summary>
    /// Counts a virtual device of one capability. The first one adds the capability when the seat does not have it.
    /// </summary>
    /// <param name="capability">One of <see cref="SeatCapability.Pointer"/>, <see cref="SeatCapability.Keyboard"/> or <see cref="SeatCapability.Touch"/>. Other values are ignored.</param>
    public void AddVirtualDevice(SeatCapability capability)
    {
        var index = SlotOf(capability);
        if (index < 0)
        {
            return;
        }

        if (_virtualDevices[index]++ == 0 && (_capabilities & capability) == 0)
        {
            _virtualOwned |= capability;
            SetCapability(capability, true);
        }
    }

    /// <summary>
    /// Removes a count that <see cref="AddVirtualDevice"/> added. The last one removes the capability, but only when a virtual device added it.
    /// </summary>
    /// <param name="capability">One of <see cref="SeatCapability.Pointer"/>, <see cref="SeatCapability.Keyboard"/> or <see cref="SeatCapability.Touch"/>. Other values are ignored.</param>
    public void RemoveVirtualDevice(SeatCapability capability)
    {
        var index = SlotOf(capability);
        if (index < 0 || _virtualDevices[index] == 0)
        {
            return;
        }

        if (--_virtualDevices[index] == 0 && (_virtualOwned & capability) != 0)
        {
            _virtualOwned &= ~capability;
            SetCapability(capability, false);
        }
    }

    private static int SlotOf(SeatCapability capability) => capability switch
    {
        SeatCapability.Pointer => 0,
        SeatCapability.Keyboard => 1,
        SeatCapability.Touch => 2,
        _ => -1,
    };

    /// <summary>
    /// Gets or sets the advertised capabilities. A change sends <c>wl_seat.capabilities</c> to every bound resource.
    /// </summary>
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

    /// <summary>
    /// Destroys the global at once and releases the keyboard state. A second call has no effect. Use <see cref="Retire"/> to remove a seat while clients run.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var resource in _resources)
        {
            ByResource.Remove(resource);
        }

        Keyboard.Dispose();
        _global.Dispose();
        BasinCounters.Untrack();
    }

    /// <summary>
    /// Withdraws the global from the registry first and disposes the seat after a grace period, so that clients that bind late do not fail.
    /// </summary>
    /// <param name="graceMillis">The maximum delay in milliseconds before the dispose.</param>
    public void Retire(int graceMillis = GlobalRetirement.DefaultGraceMillis) =>
        GlobalRetirement.Retire(Display, _global, Dispose, graceMillis);

    /// <summary>
    /// Issues a new display serial and records its kind for the serial checks. The seat keeps the last 128 serials.
    /// </summary>
    /// <param name="kind">The kind of event that the serial goes with.</param>
    /// <returns>The serial.</returns>
    public uint NextSerial(SerialKind kind)
    {
        var serial = Display.NextSerial();
        _serials[_serialCount++ % SerialHistory] = (serial, kind);
        return serial;
    }

    /// <summary>
    /// Looks up the kind of a serial that this seat issued.
    /// </summary>
    /// <param name="serial">The serial.</param>
    /// <param name="kind">The kind, or <see cref="SerialKind.Other"/> when it is not found.</param>
    /// <returns><see langword="false"/> when the serial is not among the last 128 serials.</returns>
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

    /// <summary>
    /// Checks that a serial is a recent button press, key press or touch down, whether or not it is still held. Use it for popup grabs.
    /// </summary>
    /// <param name="serial">The serial from the client request.</param>
    /// <returns><see langword="true"/> when the serial can take a grab.</returns>
    public bool ValidateGrabSerial(uint serial) =>
        TryGetSerial(serial, out var kind) &&
        kind is SerialKind.PointerButtonPress or SerialKind.KeyPress or SerialKind.TouchDown;

    /// <summary>
    /// Checks that a serial is any recent serial of this seat. Use it for selection requests.
    /// </summary>
    /// <param name="serial">The serial from the client request.</param>
    /// <returns><see langword="true"/> when the seat issued the serial recently.</returns>
    public bool ValidateSelectionSerial(uint serial) => TryGetSerial(serial, out _);

    /// <summary>
    /// Checks that a serial authorizes a pointer or touch driven grab now. Use it for interactive move, resize and drag start.
    /// </summary>
    /// <param name="serial">The serial from the client request.</param>
    /// <returns><see langword="true"/> for the press of the held implicit pointer grab, a touch down still on the surface, or a recent key press.</returns>
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

        return AddClient(client);
    }

    private SeatClient AddClient(WlClient client)
    {
        var created = new SeatClient(this, client);
        _clients.Add(created);
        client.Destroyed += () => _clients.Remove(created);
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
        var seatClient = ClientFor(client);
        var resource = new WlSeatResource(client, version, id, seatClient);
        _resources.Add(resource);
        ByResource[resource] = this;
        resource.Destroyed += (_, _) =>
        {
            _resources.Remove(resource);
            ByResource.Remove(resource);
        };
        resource.SendCapabilities((WlSeat.Capability)_capabilities);
        if (version >= 2)
        {
            resource.SendName(Name);
        }
    }
}
