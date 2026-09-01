using Tmds.DBus.Protocol;

using Basin.Diagnostics;

namespace DeskbarWm;

internal static class SessionActions
{
    public static bool Available => Directory.Exists("/run/systemd/system");

    public static void Restart(BasinLogger log) => Call("Reboot", log);

    public static void ShutDown(BasinLogger log) => Call("PowerOff", log);

    private static void Call(string method, BasinLogger log) => _ = CallAsync(method, log);

    private static MessageBuffer Request(DBusConnection connection, string method)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            "org.freedesktop.login1",
            "/org/freedesktop/login1",
            "org.freedesktop.login1.Manager",
            method,
            "b");
        writer.WriteBool(false);
        return writer.CreateMessage();
    }

    private static async Task CallAsync(string method, BasinLogger log)
    {
        try
        {
            if (DBusAddress.System is not { } address)
            {
                log.Warn($"no system bus for {method}");
                return;
            }

            using var connection = new DBusConnection(address);
            await connection.ConnectAsync();
            await connection.CallMethodAsync(Request(connection, method));
        }
        catch (Exception error) when (error is DBusExceptionBase or InvalidOperationException or IOException)
        {
            log.Warn($"logind {method} failed: {error.Message}");
        }
    }
}
