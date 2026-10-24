using Tmds.DBus.Protocol;

namespace Basin.UI.Avalonia;

internal static class AccessibilityBusProbe
{
    public static Task<string?> ReasonAsync() => Task.Run(async () =>
    {
        var direct = Environment.GetEnvironmentVariable("AT_SPI_BUS_ADDRESS");
        var session = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(direct) && string.IsNullOrEmpty(session))
        {
            return "this session has no session bus, so the accessibility bus cannot be found";
        }

        try
        {
            if (!string.IsNullOrEmpty(direct))
            {
                using var a11y = new DBusConnection(direct);
                await a11y.ConnectAsync().ConfigureAwait(false);
                return null;
            }

            using var bus = new DBusConnection(session!);
            await bus.ConnectAsync().ConfigureAwait(false);
            var address = await bus.CallMethodAsync(GetAddress(bus), static (m, _) => m.GetBodyReader().ReadString(), null).ConfigureAwait(false);
            return string.IsNullOrEmpty(address) ? "the session bus has no accessibility bus" : (string?)null;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return $"the accessibility bus is not reachable ({error.Message})";
        }
    });

    private static MessageBuffer GetAddress(DBusConnection bus)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader("org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress");
        return writer.CreateMessage();
    }
}
