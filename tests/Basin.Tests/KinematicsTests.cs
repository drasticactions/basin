using Basin.Kinematics;
using Xunit;

namespace Basin.Tests;

public sealed class KinematicsTests
{
    private static (double X, double Y, int Ticks) RunFling(Fling fling, double hz)
    {
        var interval = (long)(1e9 / hz);
        var now = 1_000_000_000L;
        var ticks = 0;
        while (fling.Step(new FrameTick(now, interval)) && ticks < 100_000)
        {
            now += interval;
            ticks++;
        }

        return (fling.X, fling.Y, ticks);
    }

    [Theory]
    [InlineData(100, 50, 900, -300)]
    [InlineData(0, 0, -2000, 0)]
    [InlineData(10, 10, 10.4, 10)]
    public void ThrowTo_comes_to_rest_at_the_target(double fromX, double fromY, double toX, double toY)
    {
        var fling = new Fling(5, 30);
        var (vx, vy) = Fling.ThrowTo(fromX, fromY, toX, toY, 5, 30);
        fling.Start(fromX, fromY, vx, vy);

        var (x, y, _) = RunFling(fling, 60);

        Assert.InRange(Math.Abs(x - toX), 0, 0.5);
        Assert.InRange(Math.Abs(y - toY), 0, 0.5);
    }

    [Fact]
    public void A_fling_rests_at_its_projected_rest()
    {
        var fling = new Fling(3, 20);
        fling.Start(0, 0, 1500, 800);
        var projected = fling.ProjectedRest;

        var (x, y, _) = RunFling(fling, 144);

        Assert.Equal(projected.X, x, 9);
        Assert.Equal(projected.Y, y, 9);
    }

    [Fact]
    public void A_fling_is_frame_rate_independent()
    {
        var fling = new Fling(4, 20);
        fling.Start(0, 0, 3000, -1200);

        var a = RunFling(fling, 60);
        var b = RunFling(fling, 144);
        var c = RunFling(fling, 165);

        Assert.InRange(Math.Abs(a.X - b.X), 0, 1);
        Assert.InRange(Math.Abs(a.X - c.X), 0, 1);
        Assert.InRange(Math.Abs(a.Y - c.Y), 0, 1);
    }

    [Fact]
    public void A_fling_below_the_stop_speed_never_moves()
    {
        var fling = new Fling(4, 20);
        fling.Start(5, 5, 10, 0);

        Assert.False(fling.IsMoving);
        Assert.Equal((5.0, 5.0), fling.ProjectedRest);
    }

    [Fact]
    public void Ticks_that_do_not_advance_do_not_move_a_fling()
    {
        var fling = new Fling(4, 20);
        fling.Start(0, 0, 1000, 0);
        fling.Step(new FrameTick(1_000_000_000, 16_666_666));
        var x = fling.X;
        fling.Step(new FrameTick(1_000_000_000, 16_666_666));

        Assert.Equal(x, fling.X);
    }

    [Fact]
    public void The_first_tick_already_moves()
    {
        var fling = new Fling(4, 20);
        fling.Start(0, 0, 1000, 0);
        fling.Step(new FrameTick(5_000_000_000, 16_666_666));

        Assert.True(fling.X > 0);
    }

    [Fact]
    public void A_spring_handoff_keeps_the_release_velocity_and_arrives()
    {
        var spring = new SpringHandoff();
        spring.Start(0, 0, 2000, 0, 100, 0);
        var now = 1_000_000_000L;
        spring.Step(new FrameTick(now, 16_666_666));

        Assert.True(spring.X > 0);
        Assert.True(spring.VelocityX > 0);

        for (var i = 0; i < 600 && spring.Step(new FrameTick(now += 16_666_666, 16_666_666)); i++)
        {
        }

        Assert.False(spring.IsMoving);
        Assert.Equal(100, spring.X);
        Assert.Equal(0, spring.Y);
    }

    [Fact]
    public void A_new_spring_motion_has_the_documented_values()
    {
        var spring = new SpringMotion();
        Assert.Equal(200.0, spring.SpringConstant);
        Assert.Equal(1.1, spring.DampingRatio);
        Assert.Equal(1.0, spring.Epsilon);
        Assert.Equal(0, spring.Position);
        Assert.False(spring.IsMoving);

        spring.Anchor = 100;
        spring.Advance(50);
        Assert.InRange(spring.Position, 0.001, 99);
        Assert.True(spring.Velocity > 0);
    }

    [Fact]
    public void A_default_spring_motion_settles_on_its_first_advance()
    {
        var springs = new SpringMotion[1];
        springs[0].Anchor = 50;
        Assert.True(springs[0].IsMoving);

        springs[0].Advance(16);
        Assert.False(springs[0].IsMoving);
        Assert.Equal(50, springs[0].Position);
        Assert.Equal(0, springs[0].Velocity);
    }

    [Fact]
    public void A_retarget_on_a_settled_handoff_springs_from_the_old_target()
    {
        var spring = new SpringHandoff();
        spring.Start(100, 0, 0, 0, 100, 0);
        Assert.False(spring.IsMoving);

        spring.Retarget(300, 0);
        Assert.True(spring.IsMoving);
        Assert.Equal(100, spring.X);
        Assert.Equal(300, spring.TargetX);

        var now = 1_000_000_000L;
        spring.Step(new FrameTick(now, 16_666_666));
        Assert.InRange(spring.X, 100.001, 299);
        for (var i = 0; i < 600 && spring.Step(new FrameTick(now += 16_666_666, 16_666_666)); i++)
        {
        }

        Assert.False(spring.IsMoving);
        Assert.Equal(300, spring.X);
    }

    [Fact]
    public void A_spring_handoff_is_deterministic()
    {
        static double Run()
        {
            var spring = new SpringHandoff(300, 0.7);
            spring.Start(0, 0, -500, 900, 40, 40);
            var now = 0L;
            for (var i = 0; i < 20; i++)
            {
                spring.Step(new FrameTick(now += 6_060_606, 6_060_606));
            }

            return spring.X + spring.Y;
        }

        Assert.Equal(BitConverter.DoubleToInt64Bits(Run()), BitConverter.DoubleToInt64Bits(Run()));
    }

    [Fact]
    public void Snap_targets_pick_the_nearest_to_the_projected_rest()
    {
        var targets = new SnapTargets();
        targets.Add(SnapTarget.Point(0, 0));
        targets.Add(SnapTarget.Point(1000, 0));
        targets.Add(SnapTarget.VerticalLine(400));

        var fling = new Fling(4, 20);
        fling.Start(0, 0, 3200, 0);
        var spring = new SpringHandoff();

        Assert.True(targets.Handoff(fling, ref spring));
        Assert.Equal(1000, spring.TargetX);
        Assert.Equal(0, spring.TargetY);
        Assert.Equal(3200, spring.VelocityX, 6);
    }

    [Fact]
    public void A_grid_target_rounds_to_the_nearest_cell()
    {
        var grid = SnapTarget.Grid(10, 20, 100, 50);

        Assert.Equal((210.0, 70.0), grid.Nearest(249, 90));
        Assert.Equal((-90.0, -30.0), grid.Nearest(-120, -40));
    }

    [Fact]
    public void Snap_targets_hold_a_fixed_capacity()
    {
        var targets = new SnapTargets();
        for (var i = 0; i < SnapTargets.Capacity; i++)
        {
            Assert.True(targets.Add(SnapTarget.Point(i, i)));
        }

        Assert.False(targets.Add(SnapTarget.Point(-1, -1)));
        Assert.Equal(SnapTargets.Capacity, targets.Count);
    }

    [Fact]
    public void Stepping_allocates_nothing()
    {
        var fling = new Fling(1, 1);
        var spring = new SpringHandoff();
        var targets = new SnapTargets();
        targets.Add(SnapTarget.Grid(0, 0, 64, 64));
        fling.Start(0, 0, 5000, 5000);
        spring.Start(0, 0, 0, 0, 1000, 1000);
        var now = 0L;
        fling.Step(new FrameTick(now, 1_000_000));
        spring.Step(new FrameTick(now, 1_000_000));

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            now += 1_000_000;
            fling.Step(new FrameTick(now, 1_000_000));
            spring.Step(new FrameTick(now, 1_000_000));
            targets.Nearest(fling.X, fling.Y, out _, out _);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
