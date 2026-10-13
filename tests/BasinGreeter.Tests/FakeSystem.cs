using System.Text;
using Basin.SystemStatus;
using Basin.Tests;
using Tmds.DBus.Protocol;

namespace BasinGreeter.Tests;

internal sealed class FakeSystem : IDisposable
{
    public const string Manager = "org.freedesktop.NetworkManager";
    public const string WifiDevice = "/org/freedesktop/NetworkManager/Devices/2";
    public const string HomePoint = "/org/freedesktop/NetworkManager/AccessPoint/1";
    public const string CafePoint = "/org/freedesktop/NetworkManager/AccessPoint/2";
    public const string OfficePoint = "/org/freedesktop/NetworkManager/AccessPoint/3";
    public const string LabPoint = "/org/freedesktop/NetworkManager/AccessPoint/4";
    public const string CorpPoint = "/org/freedesktop/NetworkManager/AccessPoint/5";
    public const string SavedHome = "/org/freedesktop/NetworkManager/Settings/1";

    private FakeSystem(PrivateBus bus, MockSystemService upower, MockSystemService network)
    {
        Bus = bus;
        UPower = upower;
        Network = network;
    }

    public PrivateBus Bus { get; }

    public MockSystemService UPower { get; }

    public MockSystemService Network { get; }

    public Func<MethodContext, bool>? AddAndActivate { get; set; }

    public static async Task<FakeSystem> StartAsync()
    {
        var bus = PrivateBus.Start();
        var upower = await MockSystemService.StartAsync(bus.Address, BatteryStatus.ServiceName);
        upower.Add(BatteryStatus.DisplayDevicePath, "org.freedesktop.UPower.Device", new()
        {
            ["Type"] = VariantValue.UInt32(2),
            ["IsPresent"] = VariantValue.Bool(true),
            ["Percentage"] = VariantValue.Double(42.6),
            ["State"] = VariantValue.UInt32(2),
            ["TimeToEmpty"] = VariantValue.Int64(3600),
            ["TimeToFull"] = VariantValue.Int64(0),
            ["IconName"] = VariantValue.String("battery-good-symbolic"),
        });
        var network = await MockSystemService.StartAsync(bus.Address, NetworkStatus.ServiceName);
        var system = new FakeSystem(bus, upower, network);
        system.FillNetwork();
        return system;
    }

    public void Dispose()
    {
        UPower.Dispose();
        Network.Dispose();
        Bus.Dispose();
    }

    private void FillNetwork()
    {
        var nm = Network;
        nm.Add(NetworkStatus.ManagerPath, Manager, new()
        {
            ["WirelessEnabled"] = VariantValue.Bool(true),
            ["WirelessHardwareEnabled"] = VariantValue.Bool(true),
            ["PrimaryConnectionType"] = VariantValue.String("802-11-wireless"),
            ["PrimaryConnection"] = VariantValue.ObjectPath(new ObjectPath("/org/freedesktop/NetworkManager/ActiveConnection/1")),
            ["Devices"] = VariantValue.Array(new ObjectPath[] { new(WifiDevice) }),
        });
        nm.Add(WifiDevice, "org.freedesktop.NetworkManager.Device", new() { ["DeviceType"] = VariantValue.UInt32(2) });
        nm.Add(WifiDevice, "org.freedesktop.NetworkManager.Device.Wireless", new()
        {
            ["AccessPoints"] = VariantValue.Array(new ObjectPath[] { new(HomePoint), new(CafePoint), new(OfficePoint), new(LabPoint), new(CorpPoint) }),
            ["ActiveAccessPoint"] = VariantValue.ObjectPath(new ObjectPath(HomePoint)),
        });
        AddPoint(HomePoint, "Home", 70, 1, 0x188);
        AddPoint(CafePoint, "Cafe", 60, 0, 0);
        AddPoint(OfficePoint, "Office", 50, 1, 0x188);
        AddPoint(LabPoint, "Lab", 45, 1, 0x588);
        AddPoint(CorpPoint, "Corp", 40, 1, 0x200);
        nm.Add(NetworkStatus.SettingsPath, "org.freedesktop.NetworkManager.Settings", []);
        nm.Add("/org/freedesktop/NetworkManager/ActiveConnection/3", "org.freedesktop.NetworkManager.Connection.Active", new() { ["State"] = VariantValue.UInt32(2) });
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

                case "AddAndActivateConnection":
                    {
                        if (AddAndActivate?.Invoke(context) == true)
                        {
                            return true;
                        }

                        using var writer = context.CreateReplyWriter("oo");
                        writer.WriteObjectPath("/org/freedesktop/NetworkManager/Settings/9");
                        writer.WriteObjectPath("/org/freedesktop/NetworkManager/ActiveConnection/3");
                        context.Reply(writer.CreateMessage());
                        return true;
                    }

                default:
                    return false;
            }
        };
    }

    private void AddPoint(string path, string ssid, byte strength, uint flags, uint rsn) =>
        Network.Add(path, "org.freedesktop.NetworkManager.AccessPoint", new()
        {
            ["Ssid"] = VariantValue.Array(Encoding.UTF8.GetBytes(ssid)),
            ["Strength"] = VariantValue.Byte(strength),
            ["Flags"] = VariantValue.UInt32(flags),
            ["WpaFlags"] = VariantValue.UInt32(0),
            ["RsnFlags"] = VariantValue.UInt32(rsn),
        });
}
