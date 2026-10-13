using System.Diagnostics;
using Basin.Tests;
using Tmds.DBus.Protocol;
using Xunit;

namespace BasinGreeter.Tests;

internal sealed class AccessibilityBus : IDisposable
{
    private readonly Process _registry;
    private readonly TempDir _runtime;

    private AccessibilityBus(PrivateBus session, TempDir runtime, Process registry)
    {
        Session = session;
        _runtime = runtime;
        _registry = registry;
    }

    public PrivateBus Session { get; }

    public string Runtime => _runtime.Path;

    public static async Task<AccessibilityBus> StartAsync()
    {
        var registryd = new[] { "/usr/lib/at-spi2-registryd", "/usr/libexec/at-spi2-registryd" }.FirstOrDefault(File.Exists);
        Assert.SkipWhen(registryd is null, "at-spi2-core is not installed");
        var session = PrivateBus.Start();
        var runtime = new TempDir();
        File.SetUnixFileMode(runtime.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using (var bus = new DBusConnection(session.Address))
        {
            await bus.ConnectAsync();
            await bus.CallMethodAsync(UpdateEnvironment(bus, runtime.Path));
        }

        var info = new ProcessStartInfo(registryd!, "--use-gnome-session") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        info.Environment["DBUS_SESSION_BUS_ADDRESS"] = session.Address;
        info.Environment["XDG_RUNTIME_DIR"] = runtime.Path;
        var registry = Process.Start(info)!;
        var bus2 = new AccessibilityBus(session, runtime, registry);
        using (var a11y = await bus2.ConnectAsync())
        {
            for (var attempt = 0; attempt < 50 && !await Owned(a11y, "org.a11y.atspi.Registry"); attempt++)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
        }

        return bus2;
    }

    public Dictionary<string, string?> Apply(Dictionary<string, string?> environment)
    {
        environment["DBUS_SESSION_BUS_ADDRESS"] = Session.Address;
        environment["XDG_RUNTIME_DIR"] = Runtime;
        return environment;
    }

    public async Task<DBusConnection> ConnectAsync()
    {
        using var sessionBus = new DBusConnection(Session.Address);
        await sessionBus.ConnectAsync();
        var address = await sessionBus.CallMethodAsync(GetAddress(sessionBus), static (m, _) => m.GetBodyReader().ReadString(), null);
        var a11y = new DBusConnection(address);
        await a11y.ConnectAsync();
        return a11y;
    }

    public void Dispose()
    {
        try
        {
            _registry.Kill();
            _registry.WaitForExit(2000);
        }
        catch (InvalidOperationException)
        {
        }

        _registry.Dispose();
        Session.Dispose();
        _runtime.Dispose();
    }

    private static Task<bool> Owned(DBusConnection bus, string name) =>
        bus.CallMethodAsync(HasOwner(bus, name), static (m, _) => m.GetBodyReader().ReadBool(), null);

    private static MessageBuffer HasOwner(DBusConnection bus, string name)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s");
        writer.WriteString(name);
        return writer.CreateMessage();
    }

    private static MessageBuffer GetAddress(DBusConnection bus)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader("org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress");
        return writer.CreateMessage();
    }

    private static MessageBuffer UpdateEnvironment(DBusConnection bus, string runtime)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "UpdateActivationEnvironment", "a{ss}");
        var map = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("XDG_RUNTIME_DIR");
        writer.WriteString(runtime);
        writer.WriteDictionaryEnd(map);
        return writer.CreateMessage();
    }
}
