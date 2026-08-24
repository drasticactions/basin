using Basin.Desktop;
using Basin.Plasma;
using Basin.Seat;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class KdeIdleTests
{
    [Fact]
    public void A_timeout_fires_idle_after_its_interval_and_resumed_on_activity()
    {
        using var host = new CompositorTestHost();
        var source = new SeatIdleSource();
        using var manager = new KdeIdleManager(host.Display, host.Loop, source);

        var proxy = Bind(host);
        var timeout = proxy.GetIdleTimeout(host.Client.Seat!, 10);
        var idled = 0;
        var resumed = 0;
        timeout.Idle += (_, _) => idled++;
        timeout.Resumed += (_, _) => resumed++;

        SettleUntil(host, () => idled == 1);
        Assert.Equal(1, idled);
        Assert.Equal(0, resumed);

        source.NotifyActivity();
        SettleUntil(host, () => resumed == 1);
        Assert.Equal(1, resumed);

        SettleUntil(host, () => idled == 2);
        Assert.Equal(2, idled);
    }

    [Fact]
    public void A_zero_timeout_fires_idle_on_the_next_loop_pass()
    {
        using var host = new CompositorTestHost();
        var source = new SeatIdleSource();
        using var manager = new KdeIdleManager(host.Display, host.Loop, source);

        var proxy = Bind(host);
        var timeout = proxy.GetIdleTimeout(host.Client.Seat!, 0);
        var idled = 0;
        timeout.Idle += (_, _) => idled++;

        host.PumpToServer();
        Assert.Equal(0, idled);

        host.Loop.DispatchIdle();
        host.PumpToClient();
        Assert.Equal(1, idled);
    }

    [Fact]
    public void Simulate_user_activity_resets_every_notification_across_protocols()
    {
        using var host = new CompositorTestHost();
        var source = new SeatIdleSource();
        using var ext = new IdleManager(host.Display, host.Loop, host.Compositor, source);
        using var kde = new KdeIdleManager(host.Display, host.Loop, source);

        var notifier = BindExt(host);
        var note = notifier.GetIdleNotification(1, host.Client.Seat!);
        var extIdled = 0;
        var extResumed = 0;
        note.Idled += (_, _) => extIdled++;
        note.Resumed += (_, _) => extResumed++;

        var proxy = Bind(host);
        var timeout = proxy.GetIdleTimeout(host.Client.Seat!, 1);
        var kdeIdled = 0;
        var kdeResumed = 0;
        timeout.Idle += (_, _) => kdeIdled++;
        timeout.Resumed += (_, _) => kdeResumed++;

        SettleUntil(host, () => extIdled == 1 && kdeIdled == 1);
        Assert.Equal(1, extIdled);
        Assert.Equal(1, kdeIdled);

        timeout.SimulateUserActivity();
        SettleUntil(host, () => extResumed == 1 && kdeResumed == 1);
        Assert.Equal(1, extResumed);
        Assert.Equal(1, kdeResumed);
    }

    [Fact]
    public void An_inhibited_seat_does_not_go_idle_and_release_starts_the_clock()
    {
        using var host = new CompositorTestHost();
        var source = new SeatIdleSource();
        using var manager = new KdeIdleManager(host.Display, host.Loop, source);
        var inhibitor = source.Inhibit();

        var proxy = Bind(host);
        var timeout = proxy.GetIdleTimeout(host.Client.Seat!, 30);
        var idled = 0;
        timeout.Idle += (_, _) => idled++;

        Settle(host);
        Settle(host);
        Assert.Equal(0, idled);

        inhibitor.Dispose();
        host.PumpToServer();
        host.Loop.Dispatch(0);
        host.PumpToClient();
        Assert.Equal(0, idled);

        SettleUntil(host, () => idled == 1);
        Assert.Equal(1, idled);
    }

    [Fact]
    public void Release_removes_the_timer_and_nothing_fires_after_it()
    {
        using var host = new CompositorTestHost();
        var source = new SeatIdleSource();
        using var manager = new KdeIdleManager(host.Display, host.Loop, source);

        var proxy = Bind(host);
        var timeout = proxy.GetIdleTimeout(host.Client.Seat!, 5);
        var idled = 0;
        timeout.Idle += (_, _) => idled++;

        timeout.Release();
        Settle(host);
        Settle(host);
        Assert.Equal(0, idled);
    }

    [Fact]
    public void Two_clients_with_different_timeouts_each_fire_at_their_own_interval()
    {
        using var host = new CompositorTestHost();
        var source = new SeatIdleSource();
        using var manager = new KdeIdleManager(host.Display, host.Loop, source);
        var other = host.ConnectClient();

        var shortProxy = Bind(host);
        var shortTimeout = shortProxy.GetIdleTimeout(host.Client.Seat!, 10);
        var shortIdled = 0;
        shortTimeout.Idle += (_, _) => shortIdled++;

        var longProxy = Bind(host, other);
        var longTimeout = longProxy.GetIdleTimeout(other.Seat!, 150);
        var longIdled = 0;
        longTimeout.Idle += (_, _) => longIdled++;

        SettleUntil(host, () => shortIdled == 1);
        Assert.Equal(1, shortIdled);
        Assert.Equal(0, longIdled);

        SettleUntil(host, () => longIdled == 1, rounds: 40);
        Assert.Equal(1, longIdled);
    }

    [Fact]
    public void Destroying_the_client_with_a_live_timeout_leaves_no_timer_behind()
    {
        using var host = new CompositorTestHost();
        var source = new SeatIdleSource();
        using var manager = new KdeIdleManager(host.Display, host.Loop, source);
        var other = host.ConnectClient();

        var proxy = Bind(host, other);
        _ = proxy.GetIdleTimeout(other.Seat!, 1000);
        host.PumpToServer();

        host.DisconnectClient(other);
        Settle(host);
    }

    private static Basin.Plasma.Protocol.OrgKdeKwinIdle Bind(CompositorTestHost host, ShmTestClient? client = null)
    {
        client ??= host.Client;
        Basin.Plasma.Protocol.OrgKdeKwinIdle? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_idle")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinIdle>(e.Name, KdeIdleManager.Version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static Basin.Desktop.Protocol.ExtIdleNotifierV1 BindExt(CompositorTestHost host)
    {
        Basin.Desktop.Protocol.ExtIdleNotifierV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_idle_notifier_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ExtIdleNotifierV1>(e.Name, IdleManager.NotifierVersion);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static void Settle(CompositorTestHost host)
    {
        for (var i = 0; i < 3; i++)
        {
            host.PumpToServer();
            host.Loop.Dispatch(20);
        }

        host.PumpToClient();
    }

    private static void SettleUntil(CompositorTestHost host, Func<bool> condition, int rounds = 20)
    {
        for (var i = 0; i < rounds && !condition(); i++)
        {
            host.PumpToServer();
            host.Loop.Dispatch(20);
            host.PumpToClient();
        }
    }
}
