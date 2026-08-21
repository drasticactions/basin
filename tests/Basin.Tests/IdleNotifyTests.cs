using Basin.Desktop;
using Basin.Seat;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class IdleNotifyTests
{
    [Fact]
    public void Input_notifications_idle_through_an_inhibitor_and_ordinary_ones_wait()
    {
        using var host = new CompositorTestHost();
        var source = new SeatIdleSource();
        using var manager = new IdleManager(host.Display, host.Loop, host.Compositor, source);

        var notifier = Bind(host);
        var ordinary = notifier.GetIdleNotification(0, host.Client.Seat!);
        var input = notifier.GetInputIdleNotification(0, host.Client.Seat!);
        var ordinaryIdled = 0;
        var inputIdled = 0;
        var ordinaryResumed = 0;
        var inputResumed = 0;
        ordinary.Idled += (_, _) => ordinaryIdled++;
        ordinary.Resumed += (_, _) => ordinaryResumed++;
        input.Idled += (_, _) => inputIdled++;
        input.Resumed += (_, _) => inputResumed++;

        var inhibitor = source.Inhibit();
        Settle(host);

        Assert.Equal(1, inputIdled);
        Assert.Equal(0, ordinaryIdled);

        inhibitor.Dispose();
        Settle(host);
        Assert.Equal(1, ordinaryIdled);
        Assert.Equal(1, inputIdled);

        source.NotifyActivity();
        Settle(host);
        Assert.Equal(1, ordinaryResumed);
        Assert.Equal(1, inputResumed);
    }

    [Fact]
    public void Activity_restarts_both_kinds()
    {
        using var host = new CompositorTestHost();
        var source = new SeatIdleSource();
        using var manager = new IdleManager(host.Display, host.Loop, host.Compositor, source);

        var notifier = Bind(host);
        var input = notifier.GetInputIdleNotification(0, host.Client.Seat!);
        var idled = 0;
        input.Idled += (_, _) => idled++;

        Settle(host);
        Assert.Equal(1, idled);

        source.NotifyActivity();
        Settle(host);
        Assert.Equal(2, idled);
    }

    private static Basin.Desktop.Protocol.ExtIdleNotifierV1 Bind(CompositorTestHost host)
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
}
