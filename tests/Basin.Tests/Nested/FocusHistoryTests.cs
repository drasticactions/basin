using Basin.Shell.Nested;
using Xunit;

namespace Basin.Tests.Nested;

public sealed class FocusHistoryTests
{
    private sealed record Window(string Name);

    private static readonly Window A = new("a");

    private static readonly Window B = new("b");

    private static readonly Window C = new("c");

    private static FocusHistory<Window> History()
    {
        var history = new FocusHistory<Window>();
        history.Touch(A);
        history.Touch(B);
        history.Touch(C);
        return history;
    }

    [Fact]
    public void Touching_moves_a_window_to_the_front()
    {
        var history = History();

        Assert.Equal([C, B, A], history.Items);
        Assert.Same(C, history.MostRecent);

        history.Touch(A);

        Assert.Equal([A, C, B], history.Items);
        Assert.Same(A, history.MostRecent);
    }

    [Fact]
    public void Removing_drops_the_window_and_tolerates_an_unknown_one()
    {
        var history = History();

        history.Remove(B);
        history.Remove(B);

        Assert.Equal([C, A], history.Items);
    }

    [Fact]
    public void Next_walks_towards_older_windows_and_wraps()
    {
        var history = History();

        Assert.Same(B, history.Next(C));
        Assert.Same(A, history.Next(B));
        Assert.Same(C, history.Next(A));
    }

    [Fact]
    public void Previous_walks_towards_newer_windows_and_wraps()
    {
        var history = History();

        Assert.Same(C, history.Previous(B));
        Assert.Same(A, history.Previous(C));
        Assert.Same(B, history.Previous(A));
    }

    [Fact]
    public void Cycling_from_nothing_starts_at_either_end()
    {
        var history = History();

        Assert.Same(C, history.Next(null));
        Assert.Same(A, history.Previous(null));
        Assert.Same(C, history.Next(new Window("unknown")));
    }

    [Fact]
    public void An_empty_history_has_nothing_to_cycle()
    {
        var history = new FocusHistory<Window>();

        Assert.Null(history.MostRecent);
        Assert.Null(history.Next(null));
        Assert.Null(history.Previous(A));
        Assert.Empty(history.Items);
    }

    [Fact]
    public void A_single_window_cycles_to_itself()
    {
        var history = new FocusHistory<Window>();
        history.Touch(A);

        Assert.Same(A, history.Next(A));
        Assert.Same(A, history.Previous(A));
    }
}
