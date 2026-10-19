using Basin.Seat;
using Basin.Seat.Backends;
using Xunit;

namespace Basin.Tests;

public sealed class TouchChromeChainTests
{
    [Fact]
    public void Each_contact_stays_with_the_chrome_that_claimed_it()
    {
        var overlay = new Recorder(claimsBelow: 100);
        var shell = new Recorder(claimsBelow: double.MaxValue);
        var chain = new TouchChromeChain { First = overlay, Then = shell };

        Assert.True(chain.TryPress(1, 0, 50, 50));
        Assert.True(chain.TryPress(2, 0, 500, 500));
        chain.Motion(1, 1, 60, 60);
        chain.Motion(2, 1, 510, 510);
        chain.Release(1, 2, 60, 60);
        chain.Release(2, 2, 510, 510);
        chain.Motion(1, 3, 70, 70);

        Assert.Equal(["press 1", "refuse 2", "motion 1", "release 1"], overlay.Events);
        Assert.Equal(["press 2", "motion 2", "release 2"], shell.Events);
    }

    [Fact]
    public void A_press_neither_claims_is_not_claimed_and_cancel_reaches_both()
    {
        var overlay = new Recorder(claimsBelow: 100);
        var chain = new TouchChromeChain { First = overlay };
        Assert.False(chain.TryPress(1, 0, 500, 500));
        chain.Motion(1, 1, 510, 510);
        Assert.Equal(["refuse 1"], overlay.Events);

        var shell = new Recorder(claimsBelow: double.MaxValue);
        chain.Then = shell;
        Assert.True(chain.TryPress(3, 0, 10, 10));
        chain.Cancel();
        chain.Release(3, 1, 10, 10);
        Assert.Equal(["refuse 1", "press 3", "cancel"], overlay.Events);
        Assert.Equal(["cancel"], shell.Events);
    }

    private sealed class Recorder(double claimsBelow) : ITouchChrome
    {
        public List<string> Events { get; } = [];

        public bool TryPress(int id, uint timeMs, double x, double y)
        {
            var claimed = x < claimsBelow;
            Events.Add($"{(claimed ? "press" : "refuse")} {id}");
            return claimed;
        }

        public void Motion(int id, uint timeMs, double x, double y) => Events.Add($"motion {id}");

        public void Release(int id, uint timeMs, double x, double y) => Events.Add($"release {id}");

        public void Cancel() => Events.Add("cancel");
    }
}
