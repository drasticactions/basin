using Tmds.DBus.Protocol;

namespace Basin.SystemStatus;

/// <summary>
/// Reads BlueZ through its object manager: the first adapter by object path, its power state, and its paired or connected devices.
/// </summary>
public sealed class BluetoothStatus : SystemStatusReader
{
    /// <summary>
    /// The bus name of BlueZ.
    /// </summary>
    public const string ServiceName = "org.bluez";

    private const string ObjectManagerInterface = "org.freedesktop.DBus.ObjectManager";
    private const string AdapterInterface = "org.bluez.Adapter1";
    private const string DeviceInterface = "org.bluez.Device1";

    private BluetoothStatus(SystemStatusBus bus)
        : base(bus, ServiceName, "Bluetooth")
    {
    }

    /// <summary>
    /// Gets a value indicating whether BlueZ reports an adapter.
    /// </summary>
    public bool HasAdapter => AdapterPath is not null;

    /// <summary>
    /// Gets the object path of the adapter that the reader uses, the first by ordinal order, or <see langword="null"/>.
    /// </summary>
    public string? AdapterPath { get; private set; }

    /// <summary>
    /// Gets the alias of the adapter, else its name, or <see langword="null"/>.
    /// </summary>
    public string? AdapterName { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the adapter is powered.
    /// </summary>
    public bool Powered { get; private set; }

    /// <summary>
    /// Gets the paired or connected devices of the adapter, connected first, then by name without case. Each read replaces the list.
    /// </summary>
    public IReadOnlyList<BluetoothDevice> Devices { get; private set; } = [];

    /// <summary>
    /// Gets a value indicating whether one device in <see cref="Devices"/> is connected.
    /// </summary>
    public bool AnyConnected
    {
        get
        {
            foreach (var device in Devices)
            {
                if (device.Connected)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private protected override string? AvailableReason => HasAdapter ? null : "BlueZ reports no adapter";

    /// <summary>
    /// Creates the reader and starts its first read in the background. The reader is created also when BlueZ is not on the bus, and then it reads as not available.
    /// </summary>
    /// <param name="bus">The shared system bus connection.</param>
    /// <param name="reason">Why no reader was created, or <see langword="null"/>.</param>
    /// <returns>The reader, or <see langword="null"/> when the bus is disposed or has no address.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bus"/> is <see langword="null"/>.</exception>
    public static BluetoothStatus? TryCreate(SystemStatusBus bus, out string? reason)
    {
        if (!CheckBus(bus, out reason))
        {
            return null;
        }

        var status = new BluetoothStatus(bus);
        status.Start();
        return status;
    }

    /// <summary>
    /// Sets the <c>Powered</c> property of the adapter. A successful call also starts a read.
    /// </summary>
    /// <param name="powered"><see langword="true"/> to power the adapter on.</param>
    /// <returns>A task that completes on the compositor thread with <see langword="true"/> when BlueZ accepted the call, or with <see langword="false"/> when it failed or there is no adapter.</returns>
    public Task<bool> SetPowered(bool powered)
    {
        if (Connection is not { } connection || AdapterPath is not { } adapter)
        {
            return Task.FromResult(false);
        }

        return CallAsync(DBusProperties.SetBool(connection, ServiceName, adapter, AdapterInterface, "Powered", powered), "set Powered");
    }

    /// <summary>
    /// Calls <c>Connect</c> on a device. A successful call also starts a read.
    /// </summary>
    /// <param name="device">The device to connect.</param>
    /// <returns>A task that completes on the compositor thread with <see langword="true"/> when BlueZ connected the device, or with <see langword="false"/> when the call failed or the reader is not connected.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public Task<bool> Connect(BluetoothDevice device) => DeviceCall(device, "Connect");

    /// <summary>
    /// Calls <c>Disconnect</c> on a device. A successful call also starts a read.
    /// </summary>
    /// <param name="device">The device to disconnect.</param>
    /// <returns>A task that completes on the compositor thread with <see langword="true"/> when BlueZ disconnected the device, or with <see langword="false"/> when the call failed or the reader is not connected.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public Task<bool> Disconnect(BluetoothDevice device) => DeviceCall(device, "Disconnect");

    private protected override MatchRule[] Rules() =>
    [
        new()
        {
            Type = MessageType.Signal,
            PathNamespace = "/org/bluez",
            Interface = DBusProperties.Interface,
            Member = "PropertiesChanged",
        },
        new()
        {
            Type = MessageType.Signal,
            Path = "/",
            Interface = ObjectManagerInterface,
        },
    ];

    private protected override async Task<Action> ReadAsync(DBusConnection connection)
    {
        var objects = await connection.CallMethodAsync(
                DBusProperties.Method(connection, ServiceName, "/", ObjectManagerInterface, "GetManagedObjects"),
                static (message, _) => ReadObjects(message), null)
            .ConfigureAwait(false);

        string? adapterPath = null;
        Dictionary<string, VariantValue>? adapter = null;
        foreach (var (path, interfaces) in objects)
        {
            if (interfaces.TryGetValue(AdapterInterface, out var properties) && (adapterPath is null || string.CompareOrdinal(path, adapterPath) < 0))
            {
                adapterPath = path;
                adapter = properties;
            }
        }

        var devices = new List<BluetoothDevice>();
        if (adapterPath is not null)
        {
            foreach (var (path, interfaces) in objects)
            {
                if (!interfaces.TryGetValue(DeviceInterface, out var properties) || DBusProperties.Path(properties, "Adapter") is { } owner && owner != adapterPath)
                {
                    continue;
                }

                var paired = DBusProperties.Bool(properties, "Paired");
                var connected = DBusProperties.Bool(properties, "Connected");
                if (!paired && !connected)
                {
                    continue;
                }

                var address = DBusProperties.String(properties, "Address") ?? "";
                var name = DBusProperties.String(properties, "Alias") ?? DBusProperties.String(properties, "Name") ?? address;
                devices.Add(new BluetoothDevice(path, name, address, DBusProperties.String(properties, "Icon"), paired, connected));
            }

            devices.Sort(static (a, b) => a.Connected != b.Connected ? (a.Connected ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        var powered = adapter is not null && DBusProperties.Bool(adapter, "Powered");
        var adapterName = adapter is null ? null : DBusProperties.String(adapter, "Alias") ?? DBusProperties.String(adapter, "Name");
        return () =>
        {
            AdapterPath = adapterPath;
            AdapterName = adapterName;
            Powered = powered;
            Devices = devices;
        };
    }

    private protected override void Clear()
    {
        AdapterPath = null;
        AdapterName = null;
        Powered = false;
        Devices = [];
    }

    private Task<bool> DeviceCall(BluetoothDevice device, string member)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (Connection is not { } connection)
        {
            return Task.FromResult(false);
        }

        return CallAsync(DBusProperties.Method(connection, ServiceName, device.Path, DeviceInterface, member), member);
    }

    private static List<(string Path, Dictionary<string, Dictionary<string, VariantValue>> Interfaces)> ReadObjects(Message message)
    {
        var result = new List<(string, Dictionary<string, Dictionary<string, VariantValue>>)>();
        var reader = message.GetBodyReader();
        var objects = reader.ReadDictionaryStart();
        while (reader.HasNext(objects))
        {
            var path = reader.ReadObjectPathAsString();
            var interfaces = new Dictionary<string, Dictionary<string, VariantValue>>(StringComparer.Ordinal);
            var entries = reader.ReadDictionaryStart();
            while (reader.HasNext(entries))
            {
                var name = reader.ReadString();
                interfaces[name] = reader.ReadDictionaryOfStringToVariantValue();
            }

            result.Add((path, interfaces));
        }

        return result;
    }
}
