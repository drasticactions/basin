using Basin.AppMenu;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class AppMenuRegistrarTests
{
    [Fact]
    public void The_registrar_owns_its_name_and_answers_for_a_registered_window()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var bus = new AppMenuBus(host.Loop, daemon.Address);
        using var registrar = new AppMenuRegistrar(bus);
        var changes = new List<uint>();
        registrar.Changed += changes.Add;
        PortalBusTests.PumpUntil(host, () => registrar.OwnsName);

        using var client = new DBusConnection(daemon.Address);
        PortalBusTests.Await(host, client.ConnectAsync().AsTask());
        var unique = client.UniqueName!;

        PortalBusTests.Await(host, Call(client, "RegisterWindow", 0x2a00007, "/MenuBar/7"));
        PortalBusTests.PumpUntil(host, () => changes.Count == 1);
        Assert.Equal(AppMenuAddress.DbusMenu(unique, "/MenuBar/7"), registrar.AddressOf(0x2a00007));

        var (service, path) = PortalBusTests.Await(host, GetMenu(client, 0x2a00007));
        Assert.Equal(unique, service);
        Assert.Equal("/MenuBar/7", path);

        PortalBusTests.Await(host, Call(client, "UnregisterWindow", 0x2a00007, null));
        PortalBusTests.PumpUntil(host, () => changes.Count == 2);
        Assert.Null(registrar.AddressOf(0x2a00007));
        Assert.Throws<DBusErrorReplyException>(() => PortalBusTests.Await(host, GetMenu(client, 0x2a00007)));
    }

    [Fact]
    public void A_second_registrar_leaves_the_name_to_the_first()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var firstBus = new AppMenuBus(host.Loop, daemon.Address);
        using var first = new AppMenuRegistrar(firstBus);
        PortalBusTests.PumpUntil(host, () => first.OwnsName);

        using var secondBus = new AppMenuBus(host.Loop, daemon.Address);
        using var second = new AppMenuRegistrar(secondBus);
        var ready = false;
        second.Ready += () => ready = true;
        PortalBusTests.PumpUntil(host, () => ready);
        Assert.False(second.OwnsName);
    }

    [Fact]
    public void A_disposed_registrar_releases_its_name()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var bus = new AppMenuBus(host.Loop, daemon.Address);
        var registrar = new AppMenuRegistrar(bus);
        PortalBusTests.PumpUntil(host, () => registrar.OwnsName);

        registrar.Dispose();
        registrar.Dispose();

        using var other = new DBusConnection(daemon.Address);
        PortalBusTests.Await(host, other.ConnectAsync().AsTask());
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var taken = false;
        while (!taken && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            taken = PortalBusTests.Await(host, other.TryRequestNameAsync(AppMenuRegistrar.BusName, RequestNameOptions.None));
            host.Loop.Dispatch(10);
        }

        Assert.True(taken, "another connection could not take the name after dispose");
    }

    private static Task Call(DBusConnection client, string member, uint window, string? path)
    {
        using var writer = client.GetMessageWriter();
        writer.WriteMethodCallHeader(AppMenuRegistrar.BusName, "/com/canonical/AppMenu/Registrar", "com.canonical.AppMenu.Registrar", member, path is null ? "u" : "uo");
        writer.WriteUInt32(window);
        if (path is not null)
        {
            writer.WriteObjectPath(path);
        }

        return client.CallMethodAsync(writer.CreateMessage());
    }

    private static Task<(string Service, string Path)> GetMenu(DBusConnection client, uint window)
    {
        using var writer = client.GetMessageWriter();
        writer.WriteMethodCallHeader(AppMenuRegistrar.BusName, "/com/canonical/AppMenu/Registrar", "com.canonical.AppMenu.Registrar", "GetMenuForWindow", "u");
        writer.WriteUInt32(window);
        return client.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) =>
        {
            var reader = m.GetBodyReader();
            return (reader.ReadString(), reader.ReadObjectPathAsString());
        }, null);
    }
}
