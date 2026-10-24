using Xunit;

namespace TabWm.Tests;

public sealed class CloserTests
{
    private sealed class Window
    {
    }

    [Fact]
    public void Every_window_gets_a_close_and_the_tab_is_done_when_the_last_one_goes()
    {
        var a = new Window();
        var b = new Window();
        var closed = new List<Window>();
        var closer = new TabCloser<Window>([a, b], 1000, 5000, closed.Add);
        Assert.Equal([a, b], closed);
        Assert.Equal(TabCloseState.Waiting, closer.State);
        closer.Gone(a);
        Assert.Equal(TabCloseState.Waiting, closer.Tick(2000));
        closer.Gone(b);
        Assert.Equal(TabCloseState.Done, closer.State);
    }

    [Fact]
    public void A_window_that_stays_past_the_timeout_refuses_the_close()
    {
        var a = new Window();
        var b = new Window();
        var closer = new TabCloser<Window>([a, b], 1000, 5000, _ => { });
        closer.Gone(a);
        Assert.Equal(TabCloseState.Waiting, closer.Tick(5999));
        Assert.Equal(TabCloseState.Refused, closer.Tick(6000));
        Assert.Equal([b], closer.Remaining);
    }

    [Fact]
    public void A_refused_close_finishes_when_the_last_window_goes_later()
    {
        var a = new Window();
        var closer = new TabCloser<Window>([a], 0, 10, _ => { });
        Assert.Equal(TabCloseState.Refused, closer.Tick(10));
        closer.Gone(a);
        Assert.Equal(TabCloseState.Done, closer.State);
    }

    [Fact]
    public void Keep_cancels_and_a_late_window_never_completes_it()
    {
        var a = new Window();
        var closer = new TabCloser<Window>([a], 0, 10, _ => { });
        closer.Tick(10);
        closer.Cancel();
        Assert.Equal(TabCloseState.Cancelled, closer.State);
        closer.Gone(a);
        Assert.Equal(TabCloseState.Cancelled, closer.State);
    }

    [Fact]
    public void An_empty_tab_is_done_at_once()
    {
        var closer = new TabCloser<Window>([], 0, 5000, _ => { });
        Assert.Equal(TabCloseState.Done, closer.State);
    }
}
