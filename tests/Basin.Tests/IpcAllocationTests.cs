using Basin.Capabilities;
using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class IpcAllocationTests
{
    private const int Rounds = 100;

    [Fact]
    public void A_title_commit_stays_within_budget()
    {
        Budgets.Require();
        var (commit, _) = Measure(subscribe: false);
        Budgets.Check("server", "title-commit", commit);
    }

    [Fact]
    public void A_title_commit_with_a_subscriber_costs_the_commit_nothing_more()
    {
        Budgets.Require();
        var (baseline, _) = Measure(subscribe: false);
        var (commit, flush) = Measure(subscribe: true);
        Assert.Equal(baseline, commit);
        Budgets.Check("server", "ipc-subscribed-commit", commit);
        Budgets.Check("server", "ipc-event-flush", flush);
    }

    [Fact]
    public void A_commit_under_a_pending_wait_idle_costs_the_commit_nothing_more()
    {
        Budgets.Require();
        var baseline = MeasureCommit(wait: false);
        var waiting = MeasureCommit(wait: true);
        Assert.Equal(baseline, waiting);
        Budgets.Check("server", "ipc-wait-idle-commit", waiting);
    }

    private static long MeasureCommit(bool wait)
    {
        var model = new AggregateToplevelModel();
        using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model));
        using var source = new XdgToplevelSource(rig.Host.Shell);
        model.Add(source);
        var host = rig.Host;
        var mapped = MappedToplevel.Map(host, host.Client);
        var peer = rig.Connect();
        if (wait)
        {
            peer.Send("""{"method":"windows/wait-idle","params":{"quiet_ms":3600000,"ignore_below":0,"timeout_ms":3600000}}""");
            rig.Pump();
        }

        long commit = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            commit = 0;
            for (var round = 0; round < Rounds; round++)
            {
                mapped.Surface.Attach(mapped.Buffer.Proxy, 0, 0);
                mapped.Surface.Damage(0, 0, 4, 4);
                mapped.Surface.Commit();
                host.Client.Display.Flush();
                host.Loop.DispatchIdle();

                var before = GC.GetAllocatedBytesForCurrentThread();
                host.Loop.Dispatch(0);
                commit += GC.GetAllocatedBytesForCurrentThread() - before;
                host.Loop.DispatchIdle();
                host.PumpToClient();
            }
        }

        return commit;
    }

    private static (long Commit, long Flush) Measure(bool subscribe)
    {
        var model = new AggregateToplevelModel();
        using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model));
        using var source = new XdgToplevelSource(rig.Host.Shell);
        model.Add(source);
        var host = rig.Host;
        var mapped = MappedToplevel.Map(host, host.Client);
        var peer = rig.Connect();
        if (subscribe)
        {
            _ = peer.Call("""{"method":"ipc/subscribe","params":{"events":["window/changed"]}}""");
        }

        string[] titles = ["even", "odd"];
        long commit = 0;
        long flush = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            commit = 0;
            flush = 0;
            for (var round = 0; round < Rounds; round++)
            {
                mapped.Toplevel.SetTitle(titles[round & 1]);
                mapped.Surface.Attach(mapped.Buffer.Proxy, 0, 0);
                mapped.Surface.Damage(0, 0, 4, 4);
                mapped.Surface.Commit();
                host.Client.Display.Flush();
                host.Loop.DispatchIdle();

                var before = GC.GetAllocatedBytesForCurrentThread();
                host.Loop.Dispatch(0);
                commit += GC.GetAllocatedBytesForCurrentThread() - before;

                before = GC.GetAllocatedBytesForCurrentThread();
                host.Loop.DispatchIdle();
                flush += GC.GetAllocatedBytesForCurrentThread() - before;

                if (subscribe)
                {
                    _ = peer.Receive();
                }

                host.PumpToClient();
            }
        }

        return (commit, flush);
    }
}
