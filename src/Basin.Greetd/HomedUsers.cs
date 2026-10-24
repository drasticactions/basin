using Tmds.DBus.Protocol;
using static Basin.Greetd.GreetdLog;

namespace Basin.Greetd;

/// <summary>
/// Asks systemd-homed for the UIDs of its users over the system bus.
/// </summary>
public static class HomedUsers
{
    /// <summary>The bus name of systemd-homed.</summary>
    public const string ServiceName = "org.freedesktop.home1";

    /// <summary>The object path of the homed manager.</summary>
    public const string ManagerPath = "/org/freedesktop/home1";

    /// <summary>The interface that has the <c>ListHomes</c> method.</summary>
    public const string ManagerInterface = "org.freedesktop.home1.Manager";

    /// <summary>
    /// Calls <c>ListHomes</c> on a new connection and closes it again. A failure does not throw. It gives an empty set and a reason.
    /// </summary>
    /// <param name="systemBusAddress">The system bus address, or <see langword="null"/> to use <c>DBUS_SYSTEM_BUS_ADDRESS</c> or the default system bus.</param>
    /// <returns>The UIDs, or an empty set and the reason when homed is not reachable.</returns>
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
            Log.Debug(error, $"homed: not reachable");
            return new HomedUids(new HashSet<uint>(), $"systemd-homed is not reachable ({error.Message})");
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
