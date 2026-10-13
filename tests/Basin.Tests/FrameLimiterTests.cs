using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class FrameLimiterTests
{
    [Theory]
    [InlineData(60_000, 30, 2)]
    [InlineData(120_000, 30, 4)]
    [InlineData(144_000, 50, 2)]
    [InlineData(60_000, 50, 1)]
    [InlineData(59_950, 60, 1)]
    [InlineData(165_000, 0, 1)]
    public void The_fixed_refresh_divisor_is_the_whole_number_of_vblanks_per_frame(int refreshMilliHz, int fps, int divisor) =>
        Assert.Equal(divisor, FrameLimitCadence.Divisor(refreshMilliHz, fps));

    [Fact]
    public void Thirty_frames_on_120_hz_open_once_every_fourth_vblank()
    {
        var opens = 0;
        for (ulong vblank = 1; vblank <= 120; vblank++)
        {
            opens += FrameLimitCadence.FixedOpens(vblank, vblank - 1, 4) ? 1 : 0;
        }

        Assert.Equal(30, opens);
    }

    [Fact]
    public void A_skipped_vblank_still_serves_the_boundary_it_crossed()
    {
        Assert.True(FrameLimitCadence.FixedOpens(9, 6, 4));
        Assert.False(FrameLimitCadence.FixedOpens(10, 9, 4));
        Assert.False(FrameLimitCadence.FixedOpens(9, 9, 4));
    }

    [Fact]
    public void The_limited_cycle_is_the_divisor_on_fixed_refresh_and_the_rate_on_variable_refresh()
    {
        Assert.Equal(33_333_332, FrameLimitCadence.LimitedCycleNanos(16_666_666, 60_000, 30, variable: false));
        Assert.Equal(13_888_888, FrameLimitCadence.LimitedCycleNanos(6_944_444, 144_000, 72, variable: true));
        Assert.Equal(16_666_666, FrameLimitCadence.LimitedCycleNanos(16_666_666, 60_000, 0, variable: true));
    }

    [Fact]
    public void A_variable_refresh_schedule_holds_72_frames_a_second_when_each_use_lands_late()
    {
        const long cycle = 1_000_000_000L / 72;
        const long refresh = 6_944_444;
        var anchor = 0L;
        var uses = 0;
        for (var now = 0L; now < 5_000_000_000; now += 50_000)
        {
            if (FrameLimitCadence.VariableOpens(now, anchor, cycle))
            {
                var used = now + 176_000;
                anchor = FrameLimitCadence.NextAnchor(used, anchor, cycle, refresh);
                uses++;
                now = used;
            }
        }

        Assert.InRange(uses, 359, 361);
    }

    [Fact]
    public void A_use_more_than_a_refresh_late_restarts_the_schedule_rather_than_catching_up()
    {
        const long cycle = 20_000_000;
        Assert.Equal(20_000_000, FrameLimitCadence.NextAnchor(20_000_000, 1, cycle - 1, 16_666_666));
        Assert.Equal(60_000_000, FrameLimitCadence.NextAnchor(60_000_000, 20_000_000, cycle, 16_666_666));
    }

    [Fact]
    public void A_limited_surface_gets_half_its_frame_callbacks_at_30_on_a_60_hz_output()
    {
        using var host = new CompositorTestHost();
        using var limiter = new FrameLimiter(host.Loop, host.Layout);
        var window = MappedToplevel.Map(host, host.Client);
        limiter.SetLimit(window.ServerSurface, 30);

        var done = 0;
        var waiting = false;
        for (var i = 0; i < 60; i++)
        {
            if (!waiting)
            {
                var callback = window.Surface.Frame();
                callback.Done += (_, _) =>
                {
                    done++;
                    waiting = false;
                };
                waiting = true;
                window.Surface.Commit();
                host.PumpToServer();
            }

            host.CommitFrame();
            host.PumpToClient();
        }

        Assert.InRange(done, 29, 31);
        limiter.SetLimit(window.ServerSurface, 0);
        Assert.False(window.ServerSurface.PacingHeld);
    }

    [Fact]
    public void The_limiter_state_is_sent_on_bind_and_again_when_a_surface_of_the_client_is_limited()
    {
        using var host = new CompositorTestHost();
        using var limiter = new FrameLimiter(host.Loop, host.Layout);
        using var manager = new GamescopeLimiterManager(host.Display, host.Compositor, limiter);
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.GamescopeLimiter? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "gamescope_limiter")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.GamescopeLimiter>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var states = new List<uint>();
        proxy!.State += (_, e) => states.Add(e.FrameLimitState);
        host.PumpUntil(() => states.Count == 1);
        Assert.Equal([0u], states);

        limiter.SetLimit(window.ServerSurface, 40);
        host.PumpUntil(() => states.Count == 2);
        limiter.SetLimit(window.ServerSurface, 0);
        host.PumpUntil(() => states.Count == 3);
        Assert.Equal([0u, 1u, 0u], states);
        proxy.Dispose();
        host.PumpToServer();
    }
}
