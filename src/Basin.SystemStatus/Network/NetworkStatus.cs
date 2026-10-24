using System.Text;
using Tmds.DBus.Protocol;
using static Basin.SystemStatus.SystemStatusLog;

namespace Basin.SystemStatus;

/// <summary>
/// Reads NetworkManager: the wireless switches, the primary connection, the first wireless device and its networks, and the saved connections. It also joins a network.
/// </summary>
public sealed class NetworkStatus : SystemStatusReader
{
    /// <summary>
    /// The bus name of NetworkManager.
    /// </summary>
    public const string ServiceName = "org.freedesktop.NetworkManager";
    /// <summary>
    /// The object path of the NetworkManager manager object.
    /// </summary>
    public const string ManagerPath = "/org/freedesktop/NetworkManager";
    /// <summary>
    /// The object path of the NetworkManager settings object.
    /// </summary>
    public const string SettingsPath = "/org/freedesktop/NetworkManager/Settings";

    private const string ManagerInterface = "org.freedesktop.NetworkManager";
    private const string DeviceInterface = "org.freedesktop.NetworkManager.Device";
    private const string WirelessInterface = "org.freedesktop.NetworkManager.Device.Wireless";
    private const string AccessPointInterface = "org.freedesktop.NetworkManager.AccessPoint";
    private const string SettingsInterface = "org.freedesktop.NetworkManager.Settings";
    private const string ConnectionInterface = "org.freedesktop.NetworkManager.Settings.Connection";
    private const string ActiveInterface = "org.freedesktop.NetworkManager.Connection.Active";
    private const string WirelessType = "802-11-wireless";
    private const uint WifiDeviceType = 2;

    private string? _wirelessDevice;

    private NetworkStatus(SystemStatusBus bus)
        : base(bus, ServiceName, "network")
    {
    }

    /// <summary>
    /// Gets a value indicating whether the software switch for wireless is on.
    /// </summary>
    public bool WirelessEnabled { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the hardware switch for wireless is on. It reads true when NetworkManager does not give the property.
    /// </summary>
    public bool WirelessHardwareEnabled { get; private set; }

    /// <summary>
    /// Gets a value indicating whether NetworkManager has a wireless device. The join requests need one.
    /// </summary>
    public bool HasWirelessDevice => _wirelessDevice is not null;

    /// <summary>
    /// Gets the type of the primary connection, for example <c>802-11-wireless</c>, or <see langword="null"/> when there is none.
    /// </summary>
    public string? PrimaryConnectionType { get; private set; }

    /// <summary>
    /// Gets the kind of the primary connection.
    /// </summary>
    public NetworkConnectionKind PrimaryKind { get; private set; }

    /// <summary>
    /// Gets the strength from 0 to 100 of the active access point when the primary connection is wireless, else <see langword="null"/>.
    /// </summary>
    public int? PrimaryStrength { get; private set; }

    /// <summary>
    /// Gets the networks of the first wireless device, one per SSID, the active network first, then by strength. Each read replaces the list.
    /// </summary>
    public IReadOnlyList<NetworkAccessPoint> AccessPoints { get; private set; } = [];

    /// <summary>
    /// Gets every saved connection profile that the reader can read. Each read replaces the list.
    /// </summary>
    public IReadOnlyList<NetworkSavedConnection> SavedConnections { get; private set; } = [];

    /// <summary>
    /// Creates the reader and starts its first read in the background. The reader is created also when NetworkManager is not on the bus, and then it reads as not available.
    /// </summary>
    /// <param name="bus">The shared system bus connection.</param>
    /// <param name="reason">Why no reader was created, or <see langword="null"/>.</param>
    /// <returns>The reader, or <see langword="null"/> when the bus is disposed or has no address.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bus"/> is <see langword="null"/>.</exception>
    public static NetworkStatus? TryCreate(SystemStatusBus bus, out string? reason)
    {
        if (!CheckBus(bus, out reason))
        {
            return null;
        }

        var status = new NetworkStatus(bus);
        status.Start();
        return status;
    }

    /// <summary>
    /// Sets the <c>WirelessEnabled</c> property of NetworkManager. A successful call also starts a read.
    /// </summary>
    /// <param name="enabled"><see langword="true"/> to turn wireless on.</param>
    /// <returns>A task that completes on the compositor thread with <see langword="true"/> when NetworkManager accepted the call, or with <see langword="false"/> when it failed or the reader is not connected.</returns>
    public Task<bool> SetWirelessEnabled(bool enabled)
    {
        if (Connection is not { } connection)
        {
            return Task.FromResult(false);
        }

        return CallAsync(DBusProperties.SetBool(connection, ServiceName, ManagerPath, ManagerInterface, "WirelessEnabled", enabled), "set WirelessEnabled");
    }

    /// <summary>
    /// Activates the saved connection of a network on the wireless device. A successful call also starts a read.
    /// </summary>
    /// <param name="accessPoint">The network to join.</param>
    /// <returns>A task that completes on the compositor thread with <see langword="true"/> when NetworkManager accepted the request, or with <see langword="false"/> when it failed, the network has no saved connection, or there is no wireless device.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="accessPoint"/> is <see langword="null"/>.</exception>
    public Task<bool> ActivateSaved(NetworkAccessPoint accessPoint)
    {
        ArgumentNullException.ThrowIfNull(accessPoint);
        if (Connection is null || accessPoint.Saved is not { } saved || _wirelessDevice is not { } device)
        {
            return Task.FromResult(false);
        }

        using var writer = Writer();
        writer.WriteMethodCallHeader(ServiceName, ManagerPath, ManagerInterface, "ActivateConnection", "ooo");
        writer.WriteObjectPath(saved.Path);
        writer.WriteObjectPath(device);
        writer.WriteObjectPath(accessPoint.Path);
        return CallAsync(writer.CreateMessage(), "ActivateConnection");
    }

    /// <summary>
    /// Adds and activates a connection with empty settings for an open network. NetworkManager fills in the rest from the access point.
    /// </summary>
    /// <param name="accessPoint">The open network to join.</param>
    /// <returns>A task that completes on the compositor thread with <see langword="true"/> when NetworkManager accepted the request, or with <see langword="false"/> when it failed, the network is not open, or there is no wireless device.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="accessPoint"/> is <see langword="null"/>.</exception>
    public Task<bool> ActivateOpen(NetworkAccessPoint accessPoint)
    {
        ArgumentNullException.ThrowIfNull(accessPoint);
        if (Connection is null || accessPoint.Security != AccessPointSecurity.Open || _wirelessDevice is not { } device)
        {
            return Task.FromResult(false);
        }

        using var writer = Writer();
        writer.WriteMethodCallHeader(ServiceName, ManagerPath, ManagerInterface, "AddAndActivateConnection", "a{sa{sv}}oo");
        var settings = writer.WriteDictionaryStart();
        writer.WriteDictionaryEnd(settings);
        writer.WriteObjectPath(device);
        writer.WriteObjectPath(accessPoint.Path);
        return CallAsync(writer.CreateMessage(), "AddAndActivateConnection");
    }

    /// <summary>
    /// Adds a system-wide connection for a WPA or WPA3 personal network and follows it until it is active or fails. On a failure, it deletes the connection that it added.
    /// </summary>
    /// <param name="accessPoint">The network to join.</param>
    /// <param name="passphrase">The passphrase as UTF-8 bytes. The method clears the span before it returns, in every case.</param>
    /// <param name="timeout">The longest time to follow the connection, or <see langword="null"/> for 30 seconds.</param>
    /// <returns>A task with the result. The task completes on the compositor thread unless the bus is disposed. A wrong passphrase gives a failed result, not an exception.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="accessPoint"/> is <see langword="null"/>.</exception>
    public Task<NetworkActivation> ActivateSecured(NetworkAccessPoint accessPoint, Span<byte> passphrase, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(accessPoint);
        if (Connection is not { } connection || _wirelessDevice is not { } device)
        {
            passphrase.Clear();
            return Task.FromResult(NetworkActivation.Failed("there is no wireless device"));
        }

        if (accessPoint.Security is not (AccessPointSecurity.Wpa or AccessPointSecurity.Wpa3))
        {
            passphrase.Clear();
            return Task.FromResult(NetworkActivation.Failed("this network is not WPA personal"));
        }

        MessageBuffer message;
        try
        {
            using var writer = Writer();
            writer.WriteMethodCallHeader(ServiceName, ManagerPath, ManagerInterface, "AddAndActivateConnection", "a{sa{sv}}oo");
            var settings = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("802-11-wireless-security");
            var security = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("key-mgmt");
            writer.WriteVariantString(accessPoint.Security == AccessPointSecurity.Wpa3 ? "sae" : "wpa-psk");
            writer.WriteDictionaryEntryStart();
            writer.WriteString("psk");
            writer.WriteVariantString((ReadOnlySpan<byte>)passphrase);
            writer.WriteDictionaryEnd(security);
            writer.WriteDictionaryEnd(settings);
            writer.WriteObjectPath(device);
            writer.WriteObjectPath(accessPoint.Path);
            message = writer.CreateMessage();
        }
        finally
        {
            passphrase.Clear();
        }

        var completion = new TaskCompletionSource<NetworkActivation>();
        _ = ActivateAsync(connection, message, device, timeout ?? TimeSpan.FromSeconds(30), completion);
        return completion.Task;
    }

    private async Task ActivateAsync(DBusConnection connection, MessageBuffer message, string device, TimeSpan timeout, TaskCompletionSource<NetworkActivation> completion)
    {
        NetworkActivation result;
        var failure = new FailureReason();
        IDisposable? watch = null;
        try
        {
            watch = await connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Path = device, Interface = DeviceInterface, Member = "StateChanged" },
                ReadFailure,
                OnFailure,
                emitOnCapturedContext: false,
                ObserverFlags.None,
                state: failure).ConfigureAwait(false);
            var (settings, active) = await connection.CallMethodAsync(message, static (reply, _) =>
            {
                var reader = reply.GetBodyReader();
                return (reader.ReadObjectPath().ToString(), reader.ReadObjectPath().ToString());
            }, null).ConfigureAwait(false);
            result = await FollowAsync(connection, active, device, timeout, failure).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                await ForgetAsync(connection, settings).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            result = NetworkActivation.Failed(error.Message);
        }
        finally
        {
            watch?.Dispose();
        }

        if (!result.Succeeded)
        {
            Log.Warn($"{Service} AddAndActivateConnection: {result.Error}");
        }

        if (Bus.IsDisposed)
        {
            completion.TrySetResult(result);
            return;
        }

        Bus.Post(() =>
        {
            completion.TrySetResult(result);
            Refresh();
        });
    }

    private static async Task ForgetAsync(DBusConnection connection, string settings)
    {
        try
        {
            await connection.CallMethodAsync(DBusProperties.Method(connection, ServiceName, settings, ConnectionInterface, "Delete")).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException error)
        {
            Log.Warn($"{ServiceName} could not remove the failed connection {settings}: {error.Message}");
        }
    }

    private static uint ReadFailure(Message message, object? state)
    {
        const uint Failed = 120;
        var reader = message.GetBodyReader();
        var next = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        var reason = reader.ReadUInt32();
        return next == Failed ? reason : 0;
    }

    private static void OnFailure(Notification<uint> notification)
    {
        if (notification.HasValue && notification.Value != 0 && notification.State is FailureReason failure)
        {
            failure.Reason = notification.Value;
        }
    }

    private static async Task<NetworkActivation> FollowAsync(DBusConnection connection, string active, string device, TimeSpan timeout, FailureReason failure)
    {
        const uint Activated = 2;
        const uint Deactivating = 3;
        const uint Deactivated = 4;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            uint state;
            try
            {
                state = DBusProperties.UInt(await DBusProperties.GetAsync(connection, ServiceName, active, ActiveInterface, "State").ConfigureAwait(false));
            }
            catch (DBusErrorReplyException)
            {
                state = Deactivated;
            }

            if (state == Activated)
            {
                return NetworkActivation.Success;
            }

            if (state is Deactivating or Deactivated || failure.Reason != 0)
            {
                var reason = failure.Reason != 0 ? failure.Reason : await ReasonAsync(connection, device).ConfigureAwait(false);
                return NetworkActivation.Failed(Describe(reason));
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        return NetworkActivation.Failed("the network did not connect in time");
    }

    private sealed class FailureReason
    {
        public volatile uint Reason;
    }

    private static async Task<uint> ReasonAsync(DBusConnection connection, string device)
    {
        uint reason = 0;
        try
        {
            var value = await DBusProperties.GetAsync(connection, ServiceName, device, DeviceInterface, "StateReason").ConfigureAwait(false);
            if (value.Type == VariantValueType.Struct && value.Count > 1)
            {
                reason = DBusProperties.UInt(value.GetItem(1));
            }
        }
        catch (DBusErrorReplyException)
        {
        }

        return reason;
    }

    private static string Describe(uint reason) => reason switch
    {
        7 => "the passphrase was not accepted",
        8 or 9 or 10 => "the network refused the connection",
        11 => "the network did not answer",
        _ => $"NetworkManager stopped the connection (reason {reason})",
    };

    internal static AccessPointSecurity SecurityOf(uint flags, uint wpaFlags, uint rsnFlags)
    {
        const uint Privacy = 0x1;
        const uint Psk = 0x100;
        const uint Ieee8021X = 0x200;
        const uint Sae = 0x400;
        const uint Owe = 0x800;
        const uint SuiteB192 = 0x2000;
        var keys = wpaFlags | rsnFlags;
        if ((keys & (Ieee8021X | SuiteB192)) != 0)
        {
            return AccessPointSecurity.Enterprise;
        }

        if ((keys & Sae) != 0)
        {
            return AccessPointSecurity.Wpa3;
        }

        if ((keys & Psk) != 0)
        {
            return AccessPointSecurity.Wpa;
        }

        if ((keys & Owe) != 0)
        {
            return AccessPointSecurity.Open;
        }

        if (keys != 0)
        {
            return AccessPointSecurity.Wpa;
        }

        return (flags & Privacy) != 0 ? AccessPointSecurity.Wep : AccessPointSecurity.Open;
    }

    internal static NetworkConnectionKind KindOf(string? type) => type switch
    {
        null or "" => NetworkConnectionKind.None,
        "802-3-ethernet" => NetworkConnectionKind.Ethernet,
        WirelessType => NetworkConnectionKind.Wireless,
        "vpn" or "wireguard" => NetworkConnectionKind.Vpn,
        _ => NetworkConnectionKind.Other,
    };

    private protected override MatchRule[] Rules() =>
    [
        new()
        {
            Type = MessageType.Signal,
            PathNamespace = ManagerPath,
            Interface = DBusProperties.Interface,
            Member = "PropertiesChanged",
        },
        new()
        {
            Type = MessageType.Signal,
            Path = SettingsPath,
            Interface = SettingsInterface,
        },
    ];

    private protected override async Task<Action> ReadAsync(DBusConnection connection)
    {
        var manager = await DBusProperties.GetAllAsync(connection, ServiceName, ManagerPath, ManagerInterface).ConfigureAwait(false);
        var wirelessEnabled = DBusProperties.Bool(manager, "WirelessEnabled");
        var hardwareEnabled = DBusProperties.Bool(manager, "WirelessHardwareEnabled", true);
        var primaryType = DBusProperties.String(manager, "PrimaryConnectionType");
        var saved = await ReadSavedAsync(connection).ConfigureAwait(false);

        string? wirelessDevice = null;
        foreach (var device in DBusProperties.Paths(manager, "Devices"))
        {
            var type = await DBusProperties.GetAsync(connection, ServiceName, device, DeviceInterface, "DeviceType").ConfigureAwait(false);
            if (DBusProperties.UInt(type) == WifiDeviceType)
            {
                wirelessDevice = device;
                break;
            }
        }

        var points = new List<NetworkAccessPoint>();
        int? primaryStrength = null;
        if (wirelessDevice is not null)
        {
            var wireless = await DBusProperties.GetAllAsync(connection, ServiceName, wirelessDevice, WirelessInterface).ConfigureAwait(false);
            var active = DBusProperties.Path(wireless, "ActiveAccessPoint");
            var paths = DBusProperties.Paths(wireless, "AccessPoints");
            var reads = new Task<Dictionary<string, VariantValue>>[paths.Length];
            for (var i = 0; i < paths.Length; i++)
            {
                reads[i] = DBusProperties.GetAllAsync(connection, ServiceName, paths[i], AccessPointInterface);
            }

            var bySsid = new Dictionary<string, NetworkAccessPoint>(StringComparer.Ordinal);
            for (var i = 0; i < paths.Length; i++)
            {
                Dictionary<string, VariantValue> point;
                try
                {
                    point = await reads[i].ConfigureAwait(false);
                }
                catch (DBusErrorReplyException)
                {
                    continue;
                }

                var ssid = Encoding.UTF8.GetString(DBusProperties.Bytes(point, "Ssid"));
                var strength = (byte)Math.Min(100u, DBusProperties.UInt(point, "Strength"));
                var isActive = paths[i] == active;
                if (isActive && primaryType == WirelessType)
                {
                    primaryStrength = strength;
                }

                if (ssid.Length == 0)
                {
                    continue;
                }

                var security = SecurityOf(DBusProperties.UInt(point, "Flags"), DBusProperties.UInt(point, "WpaFlags"), DBusProperties.UInt(point, "RsnFlags"));
                var match = saved.FirstOrDefault(s => s.Type == WirelessType && s.Ssid == ssid);
                var entry = new NetworkAccessPoint(paths[i], ssid, strength, security, isActive, match);
                if (!bySsid.TryGetValue(ssid, out var existing) || entry.IsActive || (!existing.IsActive && entry.Strength > existing.Strength))
                {
                    bySsid[ssid] = entry;
                }
            }

            points.AddRange(bySsid.Values);
            points.Sort(CompareAccessPoints);
        }

        return () =>
        {
            WirelessEnabled = wirelessEnabled;
            WirelessHardwareEnabled = hardwareEnabled;
            PrimaryConnectionType = primaryType is { Length: > 0 } ? primaryType : null;
            PrimaryKind = KindOf(primaryType);
            PrimaryStrength = primaryStrength;
            _wirelessDevice = wirelessDevice;
            AccessPoints = points;
            SavedConnections = saved;
        };
    }

    private protected override void Clear()
    {
        WirelessEnabled = false;
        WirelessHardwareEnabled = false;
        PrimaryConnectionType = null;
        PrimaryKind = NetworkConnectionKind.None;
        PrimaryStrength = null;
        _wirelessDevice = null;
        AccessPoints = [];
        SavedConnections = [];
    }

    private static async Task<List<NetworkSavedConnection>> ReadSavedAsync(DBusConnection connection)
    {
        var paths = await connection.CallMethodAsync(
                DBusProperties.Method(connection, ServiceName, SettingsPath, SettingsInterface, "ListConnections"),
                static (message, _) => message.GetBodyReader().ReadArrayOfObjectPath().Select(p => p.ToString()).ToArray(), null)
            .ConfigureAwait(false);

        var saved = new List<NetworkSavedConnection>(paths.Length);
        foreach (var path in paths)
        {
            try
            {
                var entry = await connection.CallMethodAsync(
                        DBusProperties.Method(connection, ServiceName, path, ConnectionInterface, "GetSettings"),
                        (message, _) => ReadSettings(message, path), null)
                    .ConfigureAwait(false);
                if (entry is not null)
                {
                    saved.Add(entry);
                }
            }
            catch (DBusErrorReplyException)
            {
            }
        }

        return saved;
    }

    private static int CompareAccessPoints(NetworkAccessPoint a, NetworkAccessPoint b)
    {
        if (a.IsActive != b.IsActive)
        {
            return a.IsActive ? -1 : 1;
        }

        var order = b.Strength.CompareTo(a.Strength);
        return order != 0 ? order : string.CompareOrdinal(a.Ssid, b.Ssid);
    }

    private static NetworkSavedConnection? ReadSettings(Message message, string path)
    {
        var reader = message.GetBodyReader();
        var groups = reader.ReadDictionaryStart();
        string? id = null;
        string? type = null;
        string? ssid = null;
        while (reader.HasNext(groups))
        {
            var group = reader.ReadString();
            var values = reader.ReadDictionaryOfStringToVariantValue();
            if (group == "connection")
            {
                id = DBusProperties.String(values, "id");
                type = DBusProperties.String(values, "type");
            }
            else if (group == WirelessType && DBusProperties.Bytes(values, "ssid") is { Length: > 0 } bytes)
            {
                ssid = Encoding.UTF8.GetString(bytes);
            }
        }

        return type is null ? null : new NetworkSavedConnection(path, id ?? "", type, ssid);
    }
}
