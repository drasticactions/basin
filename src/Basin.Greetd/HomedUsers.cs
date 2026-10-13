using Tmds.DBus.Protocol;
using static Basin.Greetd.GreetdLog;

namespace Basin.Greetd;

public static class HomedUsers
{
    public const string ServiceName = "org.freedesktop.home1";

    public const string ManagerPath = "/org/freedesktop/home1";

    public const string ManagerInterface = "org.freedesktop.home1.Manager";

    public static async Task<HomedUids> ListAsync(string? systemBusAddress = null)
    {
        var address = systemBusAddress
            ?? (Environment.GetEnvironmentVariable("DBUS_SYSTEM_BUS_ADDRESS") is { Length: > 0 } fromEnvironment ? fromEnvironment : DBusAddress.System);
        if (address is null)
        {
            return new HomedUids(new HashSet<uint>(), "this session has no system bus address");
        }

        try
        {
            using var connection = new DBusConnection(new DBusConnectionOptions(address) { AutoConnect = false });
            await connection.ConnectAsync().ConfigureAwait(false);
            var uids = await connection.CallMethodAsync(ListHomes(connection), static (message, _) => ReadUids(message), null).ConfigureAwait(false);
            return new HomedUids(uids, null);
        }
        catch (Exception error) when (error is DBusExceptionBase or IOException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            var reason = $"systemd-homed is not reachable ({error.Message})";
            Log.Debug($"{reason}");
            return new HomedUids(new HashSet<uint>(), reason);
        }
    }

    private static MessageBuffer ListHomes(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(ServiceName, ManagerPath, ManagerInterface, "ListHomes");
        return writer.CreateMessage();
    }

    private static HashSet<uint> ReadUids(Message message)
    {
        var uids = new HashSet<uint>();
        var reader = message.GetBodyReader();
        var array = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(array))
        {
            reader.AlignStruct();
            _ = reader.ReadString();
            uids.Add(reader.ReadUInt32());
            _ = reader.ReadString();
            _ = reader.ReadUInt32();
            _ = reader.ReadString();
            _ = reader.ReadString();
            _ = reader.ReadString();
            _ = reader.ReadObjectPath();
        }

        return uids;
    }
}
