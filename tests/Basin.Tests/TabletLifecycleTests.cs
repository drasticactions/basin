using EightWm;
using Xunit;

namespace Basin.Tests;

public sealed class TabletLifecycleTests
{
    private sealed class FakeClosable : IClosable
    {
        public FakeClosable(int pid = 1234, bool attributable = true)
        {
            Pid = pid;
            IsAttributable = attributable;
        }

        public int Pid { get; }

        public bool IsAttributable { get; }

        public int CloseRequests { get; private set; }

        public void RequestClose() => CloseRequests++;
    }

    private static readonly List<IClosable> Kill = [];

    [Fact]
    public void Closing_sends_the_event_once()
    {
        var queue = new CloseQueue();
        var app = new FakeClosable();

        Assert.True(queue.Request(app, 0));
        Assert.False(queue.Request(app, 10));

        Assert.Equal(1, app.CloseRequests);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Nothing_is_killed_before_the_grace_runs_out()
    {
        var queue = new CloseQueue { GraceMillis = 10_000 };
        queue.Request(new FakeClosable(), 0);

        Assert.Equal(0, queue.Expire(9_999, Kill));
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void The_timer_fires_at_the_grace()
    {
        var queue = new CloseQueue { GraceMillis = 10_000 };
        var app = new FakeClosable();
        queue.Request(app, 0);

        Assert.Equal(1, queue.Expire(10_000, Kill));
        Assert.Same(app, Kill[0]);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void A_client_that_went_away_is_forgotten_and_never_killed()
    {
        var queue = new CloseQueue { GraceMillis = 10 };
        var app = new FakeClosable();
        queue.Request(app, 0);

        queue.Forget(app);

        Assert.Equal(0, queue.Expire(1_000, Kill));
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void An_unattributable_window_is_not_killed()
    {
        var queue = new CloseQueue { GraceMillis = 10 };
        var x11 = new FakeClosable(pid: 4321, attributable: false);
        queue.Request(x11, 0);

        Assert.Equal(0, queue.Expire(1_000, Kill));
        Assert.Equal(1, x11.CloseRequests);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void A_window_with_no_credentials_is_not_killed()
    {
        var queue = new CloseQueue { GraceMillis = 10 };
        queue.Request(new FakeClosable(pid: 0), 0);

        Assert.Equal(0, queue.Expire(1_000, Kill));
    }

    [Fact]
    public void Every_expired_window_is_reported_in_one_pass()
    {
        var queue = new CloseQueue { GraceMillis = 10 };
        queue.Request(new FakeClosable(pid: 1), 0);
        queue.Request(new FakeClosable(pid: 2), 0);
        queue.Request(new FakeClosable(pid: 3), 5_000);

        Assert.Equal(2, queue.Expire(100, Kill));
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void The_queue_reports_whether_it_holds_a_window()
    {
        var queue = new CloseQueue();
        var app = new FakeClosable();

        Assert.False(queue.Holds(app));
        queue.Request(app, 0);
        Assert.True(queue.Holds(app));
        queue.Forget(app);
        Assert.False(queue.Holds(app));
    }
}
