using Basin.Effects;
using Basin.Kinematics;
using Xunit;

namespace Basin.Tests;

public sealed class ReducedMotionTests
{
    [Fact]
    public void Reduced_motion_zeroes_durations_and_settles_springs_and_flings_in_one_step()
    {
        try
        {
            ReducedMotion.IsOn = true;
            Assert.True(new AnimationDuration(250).IsDisabled);

            var spring = new SpringMotion(300, 0.7) { Anchor = 100 };
            spring.SetPosition(0);
            spring.Advance(16);
            Assert.Equal(100, spring.Position);
            Assert.False(spring.IsMoving);

            var fling = new Fling(Fling.DefaultFriction, Fling.DefaultStopSpeed);
            fling.Start(0, 0, 2000, 0);
            var rest = fling.ProjectedRest;
            fling.Advance(16_000_000);
            Assert.False(fling.IsMoving);
            Assert.Equal(rest, (fling.X, fling.Y));
        }
        finally
        {
            ReducedMotion.IsOn = false;
        }

        Assert.False(new AnimationDuration(250).IsDisabled);
    }
}
