using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public class GlobalRetirementTests
{
    [Fact]
    public void Retiring_an_output_removes_it_and_disposes_it_later()
    {
        using var host = new CompositorTestHost();

        var extra = host.Backend.CreateOutput(new OutputMode(64, 48, 60_000), manualFrameClock: true);
        var global = new OutputGlobal(host.Display, extra);

        var names = new List<uint>();
        var removed = new List<uint>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wl_output")
            {
                names.Add(e.Name);
            }
        };
        registry.GlobalRemove += (_, e) => removed.Add(e.Name);
        host.PumpToClient();

        var client = Assert.Single(host.Display.Clients);
        var name = global.NameFor(client);
        Assert.Contains(name, names);

        global.Retire(graceMillis: 250);

        host.PumpToClient();
        Assert.Contains(name, removed);

        Assert.Equal(name, global.NameFor(client));
        PumpPast(host, graceMillis: 250);
        Assert.Throws<ObjectDisposedException>(() => global.NameFor(client));

        extra.Destroy();
    }

    [Fact]
    public void Retiring_twice_is_harmless()
    {
        using var host = new CompositorTestHost();

        var extra = host.Backend.CreateOutput(new OutputMode(64, 48, 60_000), manualFrameClock: true);
        var global = new OutputGlobal(host.Display, extra);

        global.Retire(graceMillis: 1);
        global.Retire(graceMillis: 1);
        PumpPast(host, graceMillis: 1);

        global.Dispose();
        extra.Destroy();
    }

    [Fact]
    public void The_withdrawn_notification_disposes_without_waiting_for_the_grace()
    {
        using var host = new CompositorTestHost();

        var extra = host.Backend.CreateOutput(new OutputMode(64, 48, 60_000), manualFrameClock: true);
        var global = new OutputGlobal(host.Display, extra);
        var client = Assert.Single(host.Display.Clients);
        var probe = host.Display.CreateGlobal(Wayland.WlOutput.Interface, 4, (_, _, _) => { });
        Assert.SkipWhen(!probe.SupportsWithdrawnNotification, "libwayland does not report withdrawn globals");
        probe.Dispose();

        global.Retire(graceMillis: 60_000);
        host.PumpToClient();
        host.Loop.Dispatch(0);

        host.DisconnectClient(host.Client);
        host.Loop.Dispatch(0);
        host.Loop.Dispatch(0);

        Assert.Throws<ObjectDisposedException>(() => global.NameFor(client));
        LeakTracking.Expect(0, BasinCounters.PendingFrees);
        extra.Destroy();
    }

    [Fact]
    public void A_client_acknowledging_the_removal_ends_the_wait()
    {
        using var host = new CompositorTestHost();
        using var fixes = new FixesGlobal(host.Display);
        Assert.SkipWhen(!host.Display.SupportsFixes, "this transport does not service wl_fixes.ack_global_remove");
        Assert.True(fixes.IsPublished);

        var extra = host.Backend.CreateOutput(new OutputMode(64, 48, 60_000), manualFrameClock: true);
        var global = new OutputGlobal(host.Display, extra);

        Wayland.WlFixes? proxy = null;
        var removed = new List<uint>();
        var registry = host.Client.Registry;
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wl_fixes")
            {
                proxy = registry.Bind<Wayland.WlFixes>(e.Name, FixesGlobal.Version);
            }
        };
        registry.GlobalRemove += (_, e) => removed.Add(e.Name);
        host.PumpToClient();
        Assert.NotNull(proxy);

        var client = Assert.Single(host.Display.Clients);
        var name = global.NameFor(client);

        global.Retire(graceMillis: 600_000);
        host.PumpUntil(() => removed.Contains(name));

        proxy!.AckGlobalRemove(registry, name);
        host.PumpToServer();
        host.Loop.Dispatch(0);
        host.Loop.Dispatch(0);

        Assert.Throws<ObjectDisposedException>(() => global.NameFor(client));

        proxy.Dispose();
        host.PumpToServer();
        extra.Destroy();
    }

    private static void PumpPast(CompositorTestHost host, int graceMillis)
    {
        var deadline = Environment.TickCount64 + graceMillis + 50;
        while (Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(5);
        }

        host.Loop.Dispatch(0);
    }
}
