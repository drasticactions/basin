using System.Text;
using Tmds.DBus.Protocol;
using static Basin.SystemStatus.SystemStatusLog;

namespace Basin.SystemStatus;

public sealed class NetworkStatus : SystemStatusReader
{
    public const string ServiceName = "org.freedesktop.NetworkManager";
    public const string ManagerPath = "/org/freedesktop/NetworkManager";
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

    public bool WirelessEnabled { get; private set; }

    public bool WirelessHardwareEnabled { get; private set; }

    public bool HasWirelessDevice => _wirelessDevice is not null;

    public string? PrimaryConnectionType { get; private set; }

    public NetworkConnectionKind PrimaryKind { get; private set; }

    public int? PrimaryStrength { get; private set; }

    public IReadOnlyList<NetworkAccessPoint> AccessPoints { get; private set; } = [];

    public IReadOnlyList<NetworkSavedConnection> SavedConnections { get; private set; } = [];

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

    public Task<bool> SetWirelessEnabled(bool enabled)
    {
        if (Connection is not { } connection)
        {
            return Task.FromResult(false);
        }

        return CallAsync(DBusProperties.SetBool(connection, ServiceName, ManagerPath, ManagerInterface, "WirelessEnabled", enabled), "set WirelessEnabled");
    }

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
