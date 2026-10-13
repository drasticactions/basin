using System.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class ArrivalSchedulingTests
{
    private static void DispatchUntil(CompositorTestHost host, Func<bool> condition, int timeoutMs = 500)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < timeoutMs)
        {
            host.Loop.Dispatch(1);
        }
    }

    private static int Settle(CompositorTestHost host, OutputScheduler scheduler, Basin.Backend.Headless.HeadlessOutput output, Func<int> repaints)
    {
        scheduler.ScheduleRepaint();
        host.Loop.Dispatch(0);
        scheduler.NotifyCommitted();
        Thread.Sleep(25);
        output.StepFrame();
        return repaints();
    }

    [Fact]
    public void An_arrival_mode_repaint_with_no_flip_in_flight_fires_at_once()
    {
        using var host = new CompositorTestHost();
        using var scheduler = new OutputScheduler(host.Loop, host.Output) { ArrivalMode = true };
        var repaints = 0;
        scheduler.Repaint += () => repaints++;
        Assert.Equal(1, Settle(host, scheduler, host.Output, () => repaints));

        var clock = Stopwatch.StartNew();
        scheduler.ScheduleRepaint();
        host.Loop.Dispatch(0);
        Assert.Equal(2, repaints);
        Assert.True(clock.ElapsedMilliseconds < 5, $"took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void An_arrival_mode_repaint_asked_during_a_flip_fires_on_the_flip()
    {
        using var host = new CompositorTestHost();
        using var scheduler = new OutputScheduler(host.Loop, host.Output) { ArrivalMode = true };
        var repaints = 0;
        scheduler.Repaint += () => repaints++;
        scheduler.ScheduleRepaint();
        host.Loop.Dispatch(0);
        scheduler.NotifyCommitted();

        scheduler.ScheduleRepaint();
        Thread.Sleep(25);
        host.Loop.Dispatch(0);
        Assert.Equal(1, repaints);

        host.Output.StepFrame();
        host.Loop.Dispatch(0);
        Assert.Equal(2, repaints);
    }

    [Fact]
    public void An_arrival_mode_repaint_right_after_a_flip_completes_fires_at_once()
    {
        using var host = new CompositorTestHost();
        var fast = host.Backend.CreateOutput(new OutputMode(160, 120, 144_000), manualFrameClock: true);
        try
        {
            using var scheduler = new OutputScheduler(host.Loop, fast) { ArrivalMode = true };
            var repaints = 0;
            scheduler.Repaint += () => repaints++;
            scheduler.ScheduleRepaint();
            host.Loop.Dispatch(0);
            scheduler.NotifyCommitted();
            fast.StepFrame();

            scheduler.ScheduleRepaint();
            host.Loop.Dispatch(0);
            Assert.Equal(2, repaints);
        }
        finally
        {
            fast.Destroy();
        }
    }

    [Fact]
    public void The_render_lead_follows_the_slowest_reported_render_time()
    {
        using var host = new CompositorTestHost();
        using var scheduler = new OutputScheduler(host.Loop, host.Output);
        Assert.Equal(7_000_000, scheduler.RenderLeadNanos);

        scheduler.NotifyRenderTime(1_000_000);
        Assert.Equal(2_500_000, scheduler.RenderLeadNanos);

        scheduler.NotifyRenderTime(9_000_000);
        scheduler.NotifyRenderTime(3_000_000);
        Assert.Equal(10_500_000, scheduler.RenderLeadNanos);

        for (var i = 0; i < 120; i++)
        {
            scheduler.NotifyRenderTime(400_000);
        }

        Assert.Equal(2_000_000, scheduler.RenderLeadNanos);
    }
}
