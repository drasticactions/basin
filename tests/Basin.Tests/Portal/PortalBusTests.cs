using Basin.Portal;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class PortalBusTests
{
    internal const string BusName = "org.freedesktop.impl.portal.desktop.basintest";

    private sealed class ProbeSession(PortalBus bus, ObjectPath handle, string appId) : PortalSession(bus, handle, appId)
    {
        public int ClosedOnThread { get; private set; }

        protected override void CloseCore() => ClosedOnThread = Environment.CurrentManagedThreadId;
    }

    internal static void PumpUntil(CompositorTestHost host, Func<bool> condition, int rounds = 500)
    {
        for (var i = 0; i < rounds && !condition(); i++)
        {
            host.Loop.Dispatch(10);
        }

        Assert.True(condition(), "condition not reached while pumping the compositor loop");
    }

    internal static T Await<T>(CompositorTestHost host, Task<T> task)
    {
        PumpUntil(host, () => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    internal static void Await(CompositorTestHost host, Task task)
    {
        PumpUntil(host, () => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    internal static PortalBus StartBus(CompositorTestHost host, PrivateBus daemon)
    {
        var bus = new PortalBus(host.Loop, daemon.Address, BusName);
        bus.Start();
        Await(host, bus.Started);
        Assert.True(bus.OwnsName);
        return bus;
    }

    [Fact]
    public void The_bus_owns_its_name_and_a_child_path_shows_one_interface()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var bus = StartBus(host, daemon);
        using var frontend = Await(host, PortalTestClient.ConnectAsync(daemon.Address, BusName, asFrontend: true));

        var root = Await(host, frontend.IntrospectAsync(PortalBus.RootPath));
        Assert.DoesNotContain("org.freedesktop.impl.portal.ScreenCast", root);
        Assert.DoesNotContain("org.freedesktop.impl.portal.Session", root);

        var handle = new ObjectPath(PortalBus.RootPath + "/session/1_23/probe");
        var session = new ProbeSession(bus, handle, "org.example.App");
        bus.Register(session);
        Assert.Equal("probe", session.Id);

        var child = Await(host, frontend.IntrospectAsync(handle.ToString()));
        Assert.Contains("org.freedesktop.impl.portal.Session", child);
        Assert.DoesNotContain("org.freedesktop.impl.portal.ScreenCast", child);
        Assert.Equal(1u, Await(host, frontend.GetPropertyAsync(handle.ToString(), "org.freedesktop.impl.portal.Session", "version")).GetUInt32());

        var closedSignals = 0;
        Action<Notification> onClosed = _ => closedSignals++;
        using var watch = Await(host, frontend.Connection.WatchSignalAsync(
            BusName, handle.ToString(), "org.freedesktop.impl.portal.Session", "Closed",
            onClosed, ObserverFlags.None, emitOnCapturedContext: false).AsTask());

        var loopThread = Environment.CurrentManagedThreadId;
        Await(host, frontend.CallAsync(handle.ToString(), "org.freedesktop.impl.portal.Session", "Close"));
        Assert.True(session.IsClosed);
        Assert.Equal(loopThread, session.ClosedOnThread);
        Assert.Empty(bus.Sessions);
        PumpUntil(host, () => closedSignals == 1);

        var error = Assert.Throws<DBusErrorReplyException>(() => Await(host, frontend.CallAsync(handle.ToString(), "org.freedesktop.impl.portal.Session", "Close")));
        Assert.Equal(PortalError.UnknownObject, error.ErrorName);
    }

    [Fact]
    public void A_caller_that_is_not_the_frontend_is_denied()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var bus = StartBus(host, daemon);
        using var frontend = Await(host, PortalTestClient.ConnectAsync(daemon.Address, BusName, asFrontend: true));
        using var stranger = Await(host, PortalTestClient.ConnectAsync(daemon.Address, BusName, asFrontend: false));
        PumpUntil(host, () => bus.FrontendUniqueName is not null);
        Assert.Equal(frontend.Connection.UniqueName, bus.FrontendUniqueName);

        var handle = new ObjectPath(PortalBus.RootPath + "/session/1_23/probe");
        bus.Register(new ProbeSession(bus, handle, "org.example.App"));

        var error = Assert.Throws<DBusErrorReplyException>(() => Await(host, stranger.CallAsync(handle.ToString(), "org.freedesktop.impl.portal.Session", "Close")));
        Assert.Equal(PortalError.AccessDenied, error.ErrorName);
        Assert.Single(bus.Sessions);

        Await(host, frontend.CallAsync(handle.ToString(), "org.freedesktop.impl.portal.Session", "Close"));
        Assert.Empty(bus.Sessions);
    }

    [Fact]
    public void Closing_a_request_cancels_its_token_and_losing_the_frontend_closes_sessions()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var bus = StartBus(host, daemon);
        var frontend = Await(host, PortalTestClient.ConnectAsync(daemon.Address, BusName, asFrontend: true));
        PumpUntil(host, () => bus.FrontendUniqueName is not null);

        var requestPath = new ObjectPath(PortalBus.RootPath + "/request/1_23/r1");
        var request = bus.Track(requestPath);
        Assert.False(request.Token.IsCancellationRequested);
        Await(host, frontend.CallAsync(requestPath.ToString(), "org.freedesktop.impl.portal.Request", "Close"));
        Assert.True(request.Token.IsCancellationRequested);
        bus.Release(request);

        var session = new ProbeSession(bus, new ObjectPath(PortalBus.RootPath + "/session/1_23/s1"), "org.example.App");
        bus.Register(session);
        var lost = 0;
        bus.FrontendLost += () => lost++;
        frontend.Dispose();
        PumpUntil(host, () => lost == 1);
        Assert.True(session.IsClosed);
        Assert.Null(bus.FrontendUniqueName);
        Assert.Empty(bus.Sessions);
    }

    [Fact]
    public void Without_a_session_bus_the_pack_installs_nothing()
    {
        var saved = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", null);
        try
        {
            Assert.False(PortalBus.HasSessionBus);
            using var host = new CompositorTestHost();
            using var services = new BasinServices(host.Display, host.Loop)
                .Use(host.Layout)
                .Install(PortalPack.Default)
                .Freeze();
            Assert.Null(services.Find<PortalBus>());
            Assert.Empty(services.Modules);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", saved);
        }
    }
}
