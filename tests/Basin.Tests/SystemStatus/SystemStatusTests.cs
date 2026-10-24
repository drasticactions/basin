using System.Text;
using Basin.SystemStatus;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class SystemStatusTests
{
    private const string DisplayDevice = BatteryStatus.DisplayDevicePath;
    private const string UPowerDevice = "org.freedesktop.UPower.Device";
    private const string Manager = "org.freedesktop.NetworkManager";
    private const string EthernetDevice = "/org/freedesktop/NetworkManager/Devices/1";
    private const string WifiDevice = "/org/freedesktop/NetworkManager/Devices/2";
    private const string HomePoint = "/org/freedesktop/NetworkManager/AccessPoint/1";
    private const string CafePoint = "/org/freedesktop/NetworkManager/AccessPoint/2";
    private const string WeakHomePoint = "/org/freedesktop/NetworkManager/AccessPoint/3";
    private const string SavedHome = "/org/freedesktop/NetworkManager/Settings/1";
    private const string Adapter = "/org/bluez/hci0";
    private const string Headphones = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_01";
    private const string Keyboard = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_02";
    private const string Stranger = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_03";

    [Fact]
    public void Battery_reads_the_display_device_and_raises_changed_once_for_a_burst()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var upower = PortalBusTests.Await(host, MockSystemService.StartAsync(daemon.Address, BatteryStatus.ServiceName));
        upower.Add(DisplayDevice, UPowerDevice, new()
        {
            ["Type"] = VariantValue.UInt32(2),
            ["IsPresent"] = VariantValue.Bool(true),
            ["Percentage"] = VariantValue.Double(42.5),
            ["State"] = VariantValue.UInt32(2),
            ["TimeToEmpty"] = VariantValue.Int64(3600),
            ["TimeToFull"] = VariantValue.Int64(0),
            ["IconName"] = VariantValue.String("battery-good-symbolic"),
        });

        using var bus = new SystemStatusBus(host.Loop, new SystemStatusOptions { Address = daemon.Address });
        using var battery = BatteryStatus.TryCreate(bus, out var reason);
        Assert.NotNull(battery);
        Assert.Null(reason);
        var changes = 0;
        battery.Changed += () => changes++;
        PortalBusTests.PumpUntil(host, () => battery.IsReady);

        Assert.Equal(1, changes);
        Assert.True(battery.IsAvailable);
        Assert.True(battery.IsPresent);
        Assert.Null(battery.Reason);
        Assert.Equal(42.5, battery.Percentage);
        Assert.Equal(BatteryState.Discharging, battery.State);
        Assert.Equal(TimeSpan.FromHours(1), battery.TimeToEmpty);
        Assert.Equal("battery-good-symbolic", battery.IconName);

        changes = 0;
        upower.Set(DisplayDevice, UPowerDevice, "Percentage", VariantValue.Double(43));
        upower.Set(DisplayDevice, UPowerDevice, "State", VariantValue.UInt32(1));
        upower.Set(DisplayDevice, UPowerDevice, "TimeToFull", VariantValue.Int64(600));
        PortalBusTests.PumpUntil(host, () => changes > 0);
        Settle(host);

        Assert.Equal(1, changes);
        Assert.Equal(43, battery.Percentage);
        Assert.Equal(BatteryState.Charging, battery.State);
        Assert.Equal(TimeSpan.FromMinutes(10), battery.TimeToFull);
    }

    [Fact]
    public void A_machine_without_a_battery_reads_as_not_present_with_the_reason_named()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var upower = PortalBusTests.Await(host, MockSystemService.StartAsync(daemon.Address, BatteryStatus.ServiceName));
        upower.Add(DisplayDevice, UPowerDevice, new()
        {
            ["Type"] = VariantValue.UInt32(0),
            ["IsPresent"] = VariantValue.Bool(false),
            ["Percentage"] = VariantValue.Double(0),
            ["State"] = VariantValue.UInt32(0),
        });

        using var bus = new SystemStatusBus(host.Loop, new SystemStatusOptions { Address = daemon.Address });
        using var battery = BatteryStatus.TryCreate(bus, out _)!;
        PortalBusTests.PumpUntil(host, () => battery.IsReady);

        Assert.True(battery.IsAvailable);
        Assert.False(battery.IsPresent);
        Assert.Equal("UPower reports no battery", battery.Reason);
    }

    [Fact]
    public void Network_reads_the_primary_connection_the_access_points_and_the_saved_connections()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var nm = StartNetworkManager(host, daemon);
        using var bus = new SystemStatusBus(host.Loop, new SystemStatusOptions { Address = daemon.Address });
        using var network = NetworkStatus.TryCreate(bus, out _)!;
        var changes = 0;
        network.Changed += () => changes++;
        PortalBusTests.PumpUntil(host, () => network.IsReady);

        Assert.True(network.IsAvailable);
        Assert.True(network.WirelessEnabled);
        Assert.True(network.HasWirelessDevice);
        Assert.Equal(NetworkConnectionKind.Wireless, network.PrimaryKind);
        Assert.Equal("802-11-wireless", network.PrimaryConnectionType);
        Assert.Equal(70, network.PrimaryStrength);
        var saved = Assert.Single(network.SavedConnections);
        Assert.Equal(new NetworkSavedConnection(SavedHome, "Home", "802-11-wireless", "Home"), saved);

        Assert.Equal(2, network.AccessPoints.Count);
        var home = network.AccessPoints[0];
        Assert.Equal(new NetworkAccessPoint(HomePoint, "Home", 70, AccessPointSecurity.Wpa, true, saved), home);
        var cafe = network.AccessPoints[1];
        Assert.Equal(new NetworkAccessPoint(CafePoint, "Cafe", 40, AccessPointSecurity.Open, false, null), cafe);

        changes = 0;
        nm.Set(HomePoint, "org.freedesktop.NetworkManager.AccessPoint", "Strength", VariantValue.Byte(55));
        nm.Set(NetworkStatus.ManagerPath, Manager, "PrimaryConnectionType", VariantValue.String("802-3-ethernet"));
        PortalBusTests.PumpUntil(host, () => changes > 0);
        Settle(host);

        Assert.Equal(1, changes);
        Assert.Equal(NetworkConnectionKind.Ethernet, network.PrimaryKind);
        Assert.Null(network.PrimaryStrength);
        Assert.Equal(55, network.AccessPoints[0].Strength);
    }

    [Fact]
    public void Network_setters_reach_the_service()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var nm = StartNetworkManager(host, daemon);
        using var bus = new SystemStatusBus(host.Loop, new SystemStatusOptions { Address = daemon.Address });
        using var network = NetworkStatus.TryCreate(bus, out _)!;
        PortalBusTests.PumpUntil(host, () => network.IsReady);
        var home = network.AccessPoints.Single(a => a.Ssid == "Home");
        var cafe = network.AccessPoints.Single(a => a.Ssid == "Cafe");

        Assert.True(PortalBusTests.Await(host, network.SetWirelessEnabled(false)));
        Assert.True(PortalBusTests.Await(host, network.ActivateSaved(home)));
        Assert.True(PortalBusTests.Await(host, network.ActivateOpen(cafe)));
        Assert.False(PortalBusTests.Await(host, network.ActivateOpen(home)));
        Assert.False(PortalBusTests.Await(host, network.ActivateSaved(cafe)));

        var calls = nm.Calls().Where(c => c.Member is not "ListConnections" and not "GetSettings").ToList();
        Assert.Equal(3, calls.Count);
        Assert.Equal((NetworkStatus.ManagerPath, Manager, "Set:WirelessEnabled"), (calls[0].Path, calls[0].Interface, calls[0].Member));
        Assert.False(calls[0].Arguments[0].GetBool());
        Assert.Equal("ActivateConnection", calls[1].Member);
        Assert.Equal([SavedHome, WifiDevice, HomePoint], calls[1].Arguments.Select(a => a.GetObjectPathAsString()));
        Assert.Equal("AddAndActivateConnection", calls[2].Member);
        PortalBusTests.PumpUntil(host, () => !network.WirelessEnabled);
    }

    [Fact]
    public void Bluetooth_reads_the_adapter_and_the_paired_devices_and_its_setters_reach_bluez()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var bluez = PortalBusTests.Await(host, MockSystemService.StartAsync(daemon.Address, BluetoothStatus.ServiceName));
        bluez.Add(Adapter, "org.bluez.Adapter1", new()
        {
            ["Powered"] = VariantValue.Bool(true),
            ["Alias"] = VariantValue.String("laptop"),
        });
        AddDevice(bluez, Headphones, "Headphones", paired: true, connected: true);
        AddDevice(bluez, Keyboard, "Keyboard", paired: true, connected: false);
        AddDevice(bluez, Stranger, "Stranger", paired: false, connected: false);

        using var bus = new SystemStatusBus(host.Loop, new SystemStatusOptions { Address = daemon.Address });
        using var bluetooth = BluetoothStatus.TryCreate(bus, out _)!;
        var changes = 0;
        bluetooth.Changed += () => changes++;
        PortalBusTests.PumpUntil(host, () => bluetooth.IsReady);

        Assert.True(bluetooth.IsAvailable);
        Assert.True(bluetooth.HasAdapter);
        Assert.Equal("laptop", bluetooth.AdapterName);
        Assert.True(bluetooth.Powered);
        Assert.True(bluetooth.AnyConnected);
        Assert.Equal(["Headphones", "Keyboard"], bluetooth.Devices.Select(d => d.Name));
        Assert.Equal(new BluetoothDevice(Headphones, "Headphones", "AA:BB", "audio-headphones", true, true), bluetooth.Devices[0]);

        changes = 0;
        bluez.Set(Headphones, "org.bluez.Device1", "Connected", VariantValue.Bool(false));
        bluez.Set(Keyboard, "org.bluez.Device1", "Connected", VariantValue.Bool(true));
        PortalBusTests.PumpUntil(host, () => changes > 0);
        Settle(host);
        Assert.Equal(1, changes);
        Assert.Equal(["Keyboard", "Headphones"], bluetooth.Devices.Select(d => d.Name));

        var keyboard = bluetooth.Devices[0];
        var headphones = bluetooth.Devices[1];
        Assert.True(PortalBusTests.Await(host, bluetooth.Disconnect(keyboard)));
        Assert.True(PortalBusTests.Await(host, bluetooth.Connect(headphones)));
        Assert.True(PortalBusTests.Await(host, bluetooth.SetPowered(false)));
        var calls = bluez.Calls();
        Assert.Equal([(Keyboard, "Disconnect"), (Headphones, "Connect"), (Adapter, "Set:Powered")], calls.Select(c => (c.Path, c.Member)));
        PortalBusTests.PumpUntil(host, () => !bluetooth.Powered);
    }

    [Fact]
    public void A_bus_without_the_services_degrades_each_reader_with_the_service_named()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var bus = new SystemStatusBus(host.Loop, new SystemStatusOptions { Address = daemon.Address });
        using var battery = BatteryStatus.TryCreate(bus, out _)!;
        using var network = NetworkStatus.TryCreate(bus, out _)!;
        using var bluetooth = BluetoothStatus.TryCreate(bus, out _)!;
        var changes = 0;
        battery.Changed += () => changes++;
        network.Changed += () => changes++;
        bluetooth.Changed += () => changes++;
        PortalBusTests.PumpUntil(host, () => battery.IsReady && network.IsReady && bluetooth.IsReady);
        Settle(host);

        Assert.Equal(3, changes);
        foreach (var reader in (SystemStatusReader[])[battery, network, bluetooth])
        {
            Assert.False(reader.IsAvailable);
            Assert.Equal($"{reader.Service} is not on the system bus", reader.Reason);
            Assert.True(reader.ServiceMissing);
        }

        Assert.False(battery.IsPresent);
        Assert.Empty(network.AccessPoints);
        Assert.False(bluetooth.HasAdapter);
        Assert.False(PortalBusTests.Await(host, bluetooth.SetPowered(true)));
        Assert.False(PortalBusTests.Await(host, network.SetWirelessEnabled(true)));
    }

    [Fact]
    public void A_service_that_starts_later_brings_its_reader_up()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var bus = new SystemStatusBus(host.Loop, new SystemStatusOptions { Address = daemon.Address });
        using var battery = BatteryStatus.TryCreate(bus, out _)!;
        PortalBusTests.PumpUntil(host, () => battery.IsReady);
        Assert.False(battery.IsAvailable);

        using var upower = PortalBusTests.Await(host, MockSystemService.StartAsync(daemon.Address, BatteryStatus.ServiceName));
        upower.Add(DisplayDevice, UPowerDevice, new()
        {
            ["Type"] = VariantValue.UInt32(2),
            ["IsPresent"] = VariantValue.Bool(true),
            ["Percentage"] = VariantValue.Double(80),
            ["State"] = VariantValue.UInt32(4),
        });
        PortalBusTests.PumpUntil(host, () => battery.IsAvailable);
        Assert.False(battery.ServiceMissing);
        Assert.Equal(BatteryState.Full, battery.State);
    }

    [Fact]
    public void An_unreachable_bus_degrades_and_a_disposed_bus_refuses()
    {
        using var loop = new EpollEventLoop();
        var bus = new SystemStatusBus(loop, new SystemStatusOptions { Address = "unix:path=/nonexistent/basin-system-bus" });
        var battery = BatteryStatus.TryCreate(bus, out _)!;
        for (var i = 0; i < 500 && !battery.IsReady; i++)
        {
            loop.Dispatch(10);
        }

        Assert.True(battery.IsReady);
        Assert.False(battery.IsAvailable);
        Assert.StartsWith("the system bus is not reachable", battery.Reason);
        Assert.False(battery.ServiceMissing);
        battery.Dispose();
        bus.Dispose();

        Assert.Null(NetworkStatus.TryCreate(bus, out var reason));
        Assert.Equal("the system status bus is disposed", reason);
    }

    [Fact]
    public void The_system_bus_address_comes_from_the_environment()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        var previous = Environment.GetEnvironmentVariable("DBUS_SYSTEM_BUS_ADDRESS");
        Environment.SetEnvironmentVariable("DBUS_SYSTEM_BUS_ADDRESS", daemon.Address);
        try
        {
            using var bus = new SystemStatusBus(host.Loop);
            Assert.Equal(daemon.Address, bus.Address);
            using var explicitBus = new SystemStatusBus(host.Loop, new SystemStatusOptions { Address = "unix:path=/elsewhere" });
            Assert.Equal("unix:path=/elsewhere", explicitBus.Address);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DBUS_SYSTEM_BUS_ADDRESS", previous);
        }
    }

    [Theory]
    [InlineData(0u, 0u, 0u, AccessPointSecurity.Open)]
    [InlineData(1u, 0u, 0u, AccessPointSecurity.Wep)]
    [InlineData(1u, 0u, 0x188u, AccessPointSecurity.Wpa)]
    [InlineData(1u, 0u, 0x400u, AccessPointSecurity.Wpa3)]
    [InlineData(1u, 0u, 0x500u, AccessPointSecurity.Wpa3)]
    [InlineData(1u, 0x200u, 0x200u, AccessPointSecurity.Enterprise)]
    [InlineData(0u, 0u, 0x800u, AccessPointSecurity.Open)]
    public void Access_point_flags_map_onto_a_security_kind(uint flags, uint wpa, uint rsn, AccessPointSecurity expected) =>
        Assert.Equal(expected, NetworkStatus.SecurityOf(flags, wpa, rsn));

    [Fact]
    public void Activate_secured_sends_psk_or_sae_system_wide_and_clears_the_passphrase()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var nm = StartNetworkManager(host, daemon);
        const string Active = "/org/freedesktop/NetworkManager/ActiveConnection/7";
        nm.Add(Active, "org.freedesktop.NetworkManager.Connection.Active", new() { ["State"] = VariantValue.UInt32(2) });
        var sent = new List<Dictionary<string, Dictionary<string, VariantValue>>>();
        var previous = nm.OnMethod;
        nm.OnMethod = context =>
        {
            if (context.Request.MemberAsString != "AddAndActivateConnection")
            {
                return previous!(context);
            }

            sent.Add(ReadSettings(context.Request));
            using var writer = context.CreateReplyWriter("oo");
            writer.WriteObjectPath("/org/freedesktop/NetworkManager/Settings/9");
            writer.WriteObjectPath(Active);
            context.Reply(writer.CreateMessage());
            return true;
        };
        using var bus = new SystemStatusBus(host.Loop, new SystemStatusOptions { Address = daemon.Address });
        using var network = NetworkStatus.TryCreate(bus, out _)!;
        PortalBusTests.PumpUntil(host, () => network.IsReady);
        var home = network.AccessPoints.Single(a => a.Ssid == "Home");

        var passphrase = Encoding.UTF8.GetBytes("correct horse");
        var result = PortalBusTests.Await(host, network.ActivateSecured(home, passphrase));
        Assert.True(result.Succeeded, result.Error);
        Assert.All(passphrase, b => Assert.Equal(0, b));
        var wpa3 = home with { Security = AccessPointSecurity.Wpa3 };
        Assert.True(PortalBusTests.Await(host, network.ActivateSecured(wpa3, Encoding.UTF8.GetBytes("battery staple"))).Succeeded);

        Assert.Equal(2, sent.Count);
        Assert.Equal(["802-11-wireless-security"], sent[0].Keys);
        Assert.Equal("wpa-psk", sent[0]["802-11-wireless-security"]["key-mgmt"].GetString());
        Assert.Equal("correct horse", sent[0]["802-11-wireless-security"]["psk"].GetString());
        Assert.Equal("sae", sent[1]["802-11-wireless-security"]["key-mgmt"].GetString());
        Assert.DoesNotContain(sent, settings => settings.ContainsKey("connection"));
        Assert.DoesNotContain(nm.Calls(), call => call.Member == "Delete");

        var cafe = network.AccessPoints.Single(a => a.Ssid == "Cafe");
        Assert.False(PortalBusTests.Await(host, network.ActivateSecured(cafe, new byte[] { 1 })).Succeeded);
    }

    [Fact]
    public void Activate_secured_reports_a_refusal_and_a_wrong_key_in_words()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var nm = StartNetworkManager(host, daemon);
        const string Active = "/org/freedesktop/NetworkManager/ActiveConnection/8";
        nm.Add(Active, "org.freedesktop.NetworkManager.Connection.Active", new() { ["State"] = VariantValue.UInt32(1) });
        nm.Add(WifiDevice, "org.freedesktop.NetworkManager.Device", new()
        {
            ["DeviceType"] = VariantValue.UInt32(2),
            ["StateReason"] = VariantValue.Struct(VariantValue.UInt32(30), VariantValue.UInt32(0)),
        });
        var refuse = true;
        var previous = nm.OnMethod;
        nm.OnMethod = context =>
        {
            if (context.Request.MemberAsString != "AddAndActivateConnection")
            {
                return previous!(context);
            }

            if (refuse)
            {
                context.ReplyError("org.freedesktop.NetworkManager.PermissionDenied", "Not authorized to add this connection");
                return true;
            }

            using (var writer = context.CreateReplyWriter("oo"))
            {
                writer.WriteObjectPath("/org/freedesktop/NetworkManager/Settings/9");
                writer.WriteObjectPath(Active);
                context.Reply(writer.CreateMessage());
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                nm.Signal(WifiDevice, "org.freedesktop.NetworkManager.Device", "StateChanged", 120, 60, 7);
                nm.Signal(WifiDevice, "org.freedesktop.NetworkManager.Device", "StateChanged", 30, 120, 0);
                nm.Set(Active, "org.freedesktop.NetworkManager.Connection.Active", "State", VariantValue.UInt32(4));
            });
            return true;
        };
        using var bus = new SystemStatusBus(host.Loop, new SystemStatusOptions { Address = daemon.Address });
        using var network = NetworkStatus.TryCreate(bus, out _)!;
        PortalBusTests.PumpUntil(host, () => network.IsReady);
        var home = network.AccessPoints.Single(a => a.Ssid == "Home");

        var refused = PortalBusTests.Await(host, network.ActivateSecured(home, Encoding.UTF8.GetBytes("pw")));
        Assert.False(refused.Succeeded);
        Assert.Contains("Not authorized to add this connection", refused.Error, StringComparison.Ordinal);

        refuse = false;
        var wrong = PortalBusTests.Await(host, network.ActivateSecured(home, Encoding.UTF8.GetBytes("pw")));
        Assert.False(wrong.Succeeded);
        Assert.Contains("password wasn't accepted", wrong.Error, StringComparison.Ordinal);
        var forgotten = Assert.Single(nm.Calls(), call => call.Member == "Delete");
        Assert.Equal(("/org/freedesktop/NetworkManager/Settings/9", "org.freedesktop.NetworkManager.Settings.Connection"), (forgotten.Path, forgotten.Interface));
    }

    private static Dictionary<string, Dictionary<string, VariantValue>> ReadSettings(Message request)
    {
        var reader = request.GetBodyReader();
        var settings = new Dictionary<string, Dictionary<string, VariantValue>>();
        var groups = reader.ReadDictionaryStart();
        while (reader.HasNext(groups))
        {
            var name = reader.ReadString();
            settings[name] = reader.ReadDictionaryOfStringToVariantValue();
        }

        return settings;
    }

    private static MockSystemService StartNetworkManager(CompositorTestHost host, PrivateBus daemon)
    {
        var nm = PortalBusTests.Await(host, MockSystemService.StartAsync(daemon.Address, NetworkStatus.ServiceName));
        nm.Add(NetworkStatus.ManagerPath, Manager, new()
        {
            ["WirelessEnabled"] = VariantValue.Bool(true),
            ["WirelessHardwareEnabled"] = VariantValue.Bool(true),
            ["PrimaryConnectionType"] = VariantValue.String("802-11-wireless"),
            ["PrimaryConnection"] = VariantValue.ObjectPath(new ObjectPath("/org/freedesktop/NetworkManager/ActiveConnection/1")),
            ["Devices"] = VariantValue.Array(new ObjectPath[] { new(EthernetDevice), new(WifiDevice) }),
        });
        nm.Add(EthernetDevice, "org.freedesktop.NetworkManager.Device", new() { ["DeviceType"] = VariantValue.UInt32(1) });
        nm.Add(WifiDevice, "org.freedesktop.NetworkManager.Device", new() { ["DeviceType"] = VariantValue.UInt32(2) });
        nm.Add(WifiDevice, "org.freedesktop.NetworkManager.Device.Wireless", new()
        {
            ["AccessPoints"] = VariantValue.Array(new ObjectPath[] { new(HomePoint), new(CafePoint), new(WeakHomePoint) }),
            ["ActiveAccessPoint"] = VariantValue.ObjectPath(new ObjectPath(HomePoint)),
        });
        AddPoint(nm, HomePoint, "Home", 70, 1, 0x188);
        AddPoint(nm, CafePoint, "Cafe", 40, 0, 0);
        AddPoint(nm, WeakHomePoint, "Home", 30, 1, 0x188);
        nm.Add(NetworkStatus.SettingsPath, "org.freedesktop.NetworkManager.Settings", []);
        nm.OnMethod = context =>
        {
            switch (context.Request.MemberAsString)
            {
                case "ListConnections":
                    {
                        using var writer = context.CreateReplyWriter("ao");
                        writer.WriteArray(new ObjectPath[] { new(SavedHome) });
                        context.Reply(writer.CreateMessage());
                        return true;
                    }

                case "GetSettings":
                    {
                        using var writer = context.CreateReplyWriter("a{sa{sv}}");
                        var groups = writer.WriteDictionaryStart();
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString("connection");
                        var connection = writer.WriteDictionaryStart();
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString("id");
                        writer.WriteVariant(VariantValue.String("Home"));
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString("type");
                        writer.WriteVariant(VariantValue.String("802-11-wireless"));
                        writer.WriteDictionaryEnd(connection);
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString("802-11-wireless");
                        var wireless = writer.WriteDictionaryStart();
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString("ssid");
                        writer.WriteVariant(VariantValue.Array(Encoding.UTF8.GetBytes("Home")));
                        writer.WriteDictionaryEnd(wireless);
                        writer.WriteDictionaryEnd(groups);
                        context.Reply(writer.CreateMessage());
                        return true;
                    }

                case "ActivateConnection":
                    {
                        using var writer = context.CreateReplyWriter("o");
                        writer.WriteObjectPath("/org/freedesktop/NetworkManager/ActiveConnection/2");
                        context.Reply(writer.CreateMessage());
                        return true;
                    }

                default:
                    return false;
            }
        };
        return nm;
    }

    private static void AddPoint(MockSystemService nm, string path, string ssid, byte strength, uint flags, uint rsn) =>
        nm.Add(path, "org.freedesktop.NetworkManager.AccessPoint", new()
        {
            ["Ssid"] = VariantValue.Array(Encoding.UTF8.GetBytes(ssid)),
            ["Strength"] = VariantValue.Byte(strength),
            ["Flags"] = VariantValue.UInt32(flags),
            ["WpaFlags"] = VariantValue.UInt32(0),
            ["RsnFlags"] = VariantValue.UInt32(rsn),
        });

    private static void AddDevice(MockSystemService bluez, string path, string name, bool paired, bool connected) =>
        bluez.Add(path, "org.bluez.Device1", new()
        {
            ["Alias"] = VariantValue.String(name),
            ["Address"] = VariantValue.String("AA:BB"),
            ["Icon"] = VariantValue.String("audio-headphones"),
            ["Paired"] = VariantValue.Bool(paired),
            ["Connected"] = VariantValue.Bool(connected),
            ["Adapter"] = VariantValue.ObjectPath(new ObjectPath(Adapter)),
        });

    private static void Settle(CompositorTestHost host)
    {
        for (var i = 0; i < 30; i++)
        {
            host.Loop.Dispatch(10);
        }
    }
}
