using Basin.Diagnostics;
using Tmds.DBus.Protocol;

namespace Basin.Session;

/// <summary>
/// Calls the power methods of the logind manager on the system D-Bus. Every call opens its own connection and reports failure as <see langword="false"/>.
/// </summary>
public static class LogindActions
{
    private const string Service = "org.freedesktop.login1";
    private const string ObjectPath = "/org/freedesktop/login1";
    private const string Interface = "org.freedesktop.login1.Manager";

    private static readonly BasinLogger Log = BasinLog.For("logind");

    /// <summary>
    /// Gets a value indicating whether systemd is the running init system. This value is <see langword="false"/> when the directory "/run/systemd/system" does not exist.
    /// </summary>
    public static bool Available => Directory.Exists("/run/systemd/system");

    /// <summary>
    /// Asks logind to do a power action without an interactive authorization prompt.
    /// </summary>
    /// <param name="action">The power action to do.</param>
    /// <param name="address">The D-Bus address to use, or <see langword="null"/> for the system bus.</param>
    /// <returns><see langword="true"/> when logind accepted the call, and <see langword="false"/> when no bus is available or the call failed.</returns>
    public static Task<bool> RunAsync(LogindAction action, string? address = null) => CallAsync(Method(action), address);

    /// <summary>
    /// Asks logind whether the caller can do a power action.
    /// </summary>
    /// <param name="action">The power action to ask about.</param>
    /// <param name="address">The D-Bus address to use, or <see langword="null"/> for the system bus.</param>
    /// <returns><see langword="true"/> when logind answers "yes" or "challenge", and <see langword="false"/> for any other answer or a failed call.</returns>
    public static async Task<bool> CanAsync(LogindAction action, string? address = null)
    {
        try
        {
            if ((address ?? DBusAddress.System) is not { } bus)
            {
                return false;
            }

            using var connection = new DBusConnection(bus);
            await connection.ConnectAsync().ConfigureAwait(false);
            var answer = await connection.CallMethodAsync(
                CanRequest(connection, "Can" + Method(action)),
                static (message, _) => message.GetBodyReader().ReadString(),
                null).ConfigureAwait(false);
            return answer is "yes" or "challenge";
        }
        catch (Exception error) when (error is DBusExceptionBase or InvalidOperationException or IOException)
        {
            Log.Debug(error, $"Can{Method(action)} failed");
            return false;
        }
    }

    private static string Method(LogindAction action) => action switch
    {
        LogindAction.Reboot => "Reboot",
        LogindAction.Suspend => "Suspend",
        _ => "PowerOff",
    };

    private static MessageBuffer Request(DBusConnection connection, string method)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service, ObjectPath, Interface, method, "b");
        writer.WriteBool(false);
        return writer.CreateMessage();
    }

    private static MessageBuffer CanRequest(DBusConnection connection, string method)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service, ObjectPath, Interface, method);
        return writer.CreateMessage();
    }

    private static async Task<bool> CallAsync(string method, string? address)
    {
        try
        {
            if ((address ?? DBusAddress.System) is not { } bus)
            {
                Log.Warn($"{method} failed, no system bus");
                return false;
            }

            using var connection = new DBusConnection(bus);
            await connection.ConnectAsync().ConfigureAwait(false);
            await connection.CallMethodAsync(Request(connection, method)).ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (error is DBusExceptionBase or InvalidOperationException or IOException)
        {
            Log.Warn(error, $"{method} failed");
            return false;
        }
    }
}
