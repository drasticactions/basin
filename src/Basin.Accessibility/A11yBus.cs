using Tmds.DBus.Protocol;

namespace Basin.Accessibility;

internal static class A11yBus
{
    public static async Task<DBusConnection> ConnectAsync(string sessionAddress)
    {
        var address = Environment.GetEnvironmentVariable("AT_SPI_BUS_ADDRESS");
        if (string.IsNullOrEmpty(address))
        {
            using var session = new DBusConnection(sessionAddress);
            await session.ConnectAsync().ConfigureAwait(false);
            address = await session.CallMethodAsync(Call(session, "org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress"), static (m, _) => m.GetBodyReader().ReadString(), null).ConfigureAwait(false);
        }

        var a11y = new DBusConnection(address);
        try
        {
            await a11y.ConnectAsync().ConfigureAwait(false);
        }
        catch
        {
            a11y.Dispose();
            throw;
        }

        return a11y;
    }

    public static async Task<uint> PidOfAsync(DBusConnection a11y, string bus)
    {
        try
        {
            return await a11y.CallMethodAsync(PidQuery(a11y, bus), static (m, _) => m.GetBodyReader().ReadUInt32(), null).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            return 0;
        }
    }

    private static MessageBuffer PidQuery(DBusConnection connection, string bus)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetConnectionUnixProcessID", "s");
        writer.WriteString(bus);
        return writer.CreateMessage();
    }

    public static MessageBuffer Call(DBusConnection connection, string bus, string path, string @interface, string member)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(bus, path, @interface, member);
        return writer.CreateMessage();
    }
}
