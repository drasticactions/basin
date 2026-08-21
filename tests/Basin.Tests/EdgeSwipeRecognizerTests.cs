using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public class EdgeSwipeRecognizerTests
{
    private const double Width = 1000;
    private const double Height = 800;

    private static EdgeSwipeRecognizer Candidate(
        EdgeSwipeRecognizer? recognizer = null, double x = 4, double y = 400, uint timeMs = 0)
    {
        var edges = recognizer ?? new EdgeSwipeRecognizer();
        Assert.Equal(EdgeSwipeAction.Withhold, edges.Begin(1, x, y, Width, Height, timeMs));
        return edges;
    }

    private static EdgeSwipeRecognizer Claimed(EdgeSwipeRecognizer? recognizer = null)
    {
        var edges = Candidate(recognizer);
        Assert.Equal(EdgeSwipeAction.Claim, edges.Update(1, 40, 400, 20));
        return edges;
    }

    [Fact]
    public void A_contact_starting_inside_the_band_is_withheld()
    {
        var edges = new EdgeSwipeRecognizer();

        Assert.Equal(EdgeSwipeAction.Withhold, edges.Begin(1, 4, 400, Width, Height, 0));

        Assert.True(edges.IsCandidate);
        Assert.Equal(ScreenEdge.Left, edges.Edge);
    }

    [Fact]
    public void A_contact_starting_outside_the_band_is_ignored()
    {
        var edges = new EdgeSwipeRecognizer();

        Assert.Equal(EdgeSwipeAction.Ignore, edges.Begin(1, 500, 400, Width, Height, 0));

        Assert.False(edges.IsCandidate);
        Assert.Equal(ScreenEdge.None, edges.Edge);
    }

    [Theory]
    [InlineData(2, 400, ScreenEdge.Left)]
    [InlineData(998, 400, ScreenEdge.Right)]
    [InlineData(500, 2, ScreenEdge.Top)]
    [InlineData(500, 798, ScreenEdge.Bottom)]
    public void The_first_sample_names_the_edge(double x, double y, ScreenEdge expected)
    {
        var edges = new EdgeSwipeRecognizer();

        Assert.Equal(EdgeSwipeAction.Withhold, edges.Begin(1, x, y, Width, Height, 0));

        Assert.Equal(expected, edges.Edge);
    }

    [Fact]
    public void A_corner_takes_the_nearer_edge()
    {
        var edges = new EdgeSwipeRecognizer();

        edges.Begin(1, 12, 3, Width, Height, 0);

        Assert.Equal(ScreenEdge.Top, edges.Edge);
    }

    [Fact]
    public void A_disabled_edge_is_never_a_candidate()
    {
        var edges = new EdgeSwipeRecognizer { Edges = ScreenEdges.Right };

        Assert.Equal(EdgeSwipeAction.Ignore, edges.Begin(1, 4, 400, Width, Height, 0));
    }

    [Fact]
    public void The_band_scales_with_the_output()
    {
        var edges = new EdgeSwipeRecognizer { Scale = 2 };

        Assert.Equal(EdgeSwipeAction.Withhold, edges.Begin(1, 32, 400, Width, Height, 0));
    }

    [Fact]
    public void A_second_contact_is_delivered_normally()
    {
        var edges = Candidate();

        Assert.Equal(EdgeSwipeAction.Ignore, edges.Begin(2, 4, 200, Width, Height, 10));
        Assert.Equal(EdgeSwipeAction.Ignore, edges.Update(2, 60, 200, 20));
    }

    [Fact]
    public void The_claim_needs_the_commit_distance()
    {
        var edges = Candidate();

        Assert.Equal(EdgeSwipeAction.Withhold, edges.Update(1, 9, 400, 10));
        Assert.False(edges.IsClaimed);

        Assert.Equal(EdgeSwipeAction.Claim, edges.Update(1, 13, 400, 20));
        Assert.True(edges.IsClaimed);
    }

    [Fact]
    public void A_claim_drops_the_withheld_samples()
    {
        var edges = Candidate();
        edges.Update(1, 8, 400, 10);

        Assert.Equal(EdgeSwipeAction.Claim, edges.Update(1, 40, 400, 20));

        Span<EdgeSwipeSample> replay = stackalloc EdgeSwipeSample[EdgeSwipeRecognizer.WithheldCapacity];
        Assert.Equal(0, edges.TakeWithheld(replay));
    }

    [Fact]
    public void A_declined_candidate_gives_its_samples_back_in_order()
    {
        var edges = Candidate(x: 4, y: 400);
        Assert.Equal(EdgeSwipeAction.Withhold, edges.Update(1, 5, 410, 10));

        Assert.Equal(EdgeSwipeAction.Decline, edges.Update(1, 5, 460, 20));

        Span<EdgeSwipeSample> replay = stackalloc EdgeSwipeSample[EdgeSwipeRecognizer.WithheldCapacity];
        var count = edges.TakeWithheld(replay);
        Assert.Equal(2, count);
        Assert.True(replay[0].Down);
        Assert.Equal(0u, replay[0].TimeMs);
        Assert.Equal(4, replay[0].X);
        Assert.False(replay[1].Down);
        Assert.Equal(10u, replay[1].TimeMs);
        Assert.Equal(410, replay[1].Y);
    }

    [Fact]
    public void An_undecided_candidate_is_declined_when_the_hold_back_runs_out()
    {
        var edges = Candidate();

        Assert.Equal(EdgeSwipeAction.Withhold, edges.Update(1, 6, 400, 50));
        Assert.Equal(EdgeSwipeAction.Decline, edges.Update(1, 7, 400, 200));
    }

    [Fact]
    public void A_candidate_released_before_the_claim_replays_the_release_too()
    {
        var edges = Candidate();

        Assert.Equal(EdgeSwipeAction.Decline, edges.End(1, 30));

        Span<EdgeSwipeSample> replay = stackalloc EdgeSwipeSample[EdgeSwipeRecognizer.WithheldCapacity];
        var count = edges.TakeWithheld(replay);
        Assert.Equal(2, count);
        Assert.True(replay[0].Down);
        Assert.False(replay[1].Down);
        Assert.Equal(30u, replay[1].TimeMs);
    }

    [Fact]
    public void Progress_is_the_distance_from_the_edge_over_the_reveal()
    {
        var edges = Claimed(new EdgeSwipeRecognizer { RevealDistance = 100 });

        edges.Update(1, 75, 400, 40);

        Assert.Equal(0.75, edges.Progress, 6);
    }

    [Fact]
    public void Progress_stops_at_one()
    {
        var edges = Claimed(new EdgeSwipeRecognizer { RevealDistance = 100 });

        edges.Update(1, 400, 400, 40);

        Assert.Equal(1, edges.Progress);
    }

    [Fact]
    public void A_swipe_in_and_released_in_is_the_previous_app()
    {
        var edges = Claimed(new EdgeSwipeRecognizer { RevealDistance = 100 });

        edges.Update(1, 90, 400, 60);

        Assert.Equal(EdgeSwipeAction.Finish, edges.End(1, 200));
        Assert.Equal(EdgeSwipeOutcome.In, edges.Outcome);
    }

    [Fact]
    public void A_swipe_in_and_back_out_is_the_switcher()
    {
        var edges = Claimed(new EdgeSwipeRecognizer { RevealDistance = 100 });

        edges.Update(1, 90, 400, 60);
        edges.Update(1, 10, 400, 120);

        Assert.Equal(EdgeSwipeAction.Finish, edges.End(1, 200));
        Assert.Equal(EdgeSwipeOutcome.InAndBack, edges.Outcome);
    }

    [Fact]
    public void A_swipe_held_near_the_hold_fraction_is_the_snap()
    {
        var edges = Claimed(new EdgeSwipeRecognizer { RevealDistance = 100 });

        edges.Update(1, 33, 400, 60);
        edges.Update(1, 34, 400, 700);

        Assert.Equal(EdgeSwipeAction.Finish, edges.End(1, 720));
        Assert.Equal(EdgeSwipeOutcome.Hold, edges.Outcome);
    }

    [Fact]
    public void A_short_slow_swipe_settles_back()
    {
        var edges = Claimed(new EdgeSwipeRecognizer { RevealDistance = 400 });

        edges.Update(1, 60, 400, 500);

        Assert.Equal(EdgeSwipeAction.Finish, edges.End(1, 900));
        Assert.Equal(EdgeSwipeOutcome.Cancelled, edges.Outcome);
    }

    [Fact]
    public void A_short_fast_swipe_is_a_fling()
    {
        var edges = Claimed(new EdgeSwipeRecognizer { RevealDistance = 1000 });

        edges.Update(1, 200, 400, 30);
        edges.Update(1, 260, 400, 40);

        Assert.True(edges.Progress < 0.5);
        Assert.Equal(EdgeSwipeAction.Finish, edges.End(1, 50));
        Assert.Equal(EdgeSwipeOutcome.In, edges.Outcome);
    }

    [Fact]
    public void A_cancelled_gesture_reports_cancelled()
    {
        var edges = Claimed(new EdgeSwipeRecognizer { RevealDistance = 100 });
        edges.Update(1, 90, 400, 60);

        Assert.Equal(EdgeSwipeAction.Finish, edges.End(1, 80, cancelled: true));
        Assert.Equal(EdgeSwipeOutcome.Cancelled, edges.Outcome);
    }

    [Theory]
    [InlineData(500, 780, EdgeSwipeZone.Bottom)]
    [InlineData(100, 300, EdgeSwipeZone.Left)]
    [InlineData(900, 300, EdgeSwipeZone.Right)]
    [InlineData(500, 300, EdgeSwipeZone.Middle)]
    public void A_top_edge_drag_reports_where_it_was_released(double x, double y, EdgeSwipeZone expected)
    {
        var edges = new EdgeSwipeRecognizer();
        Assert.Equal(EdgeSwipeAction.Withhold, edges.Begin(1, 500, 2, Width, Height, 0));
        Assert.Equal(EdgeSwipeAction.Claim, edges.Update(1, 500, 40, 20));

        edges.Update(1, x, y, 200);

        Assert.Equal(EdgeSwipeAction.Finish, edges.End(1, 240));
        Assert.Equal(ScreenEdge.Top, edges.Edge);
        Assert.Equal(expected, edges.Zone);
    }

    [Fact]
    public void A_pan_along_the_edge_is_declined()
    {
        var edges = Candidate();

        Assert.Equal(EdgeSwipeAction.Decline, edges.Update(1, 6, 480, 20));
    }

    [Fact]
    public void Abort_forgets_the_gesture_and_the_samples()
    {
        var edges = Candidate();

        edges.Abort();

        Assert.False(edges.IsCandidate);
        Span<EdgeSwipeSample> replay = stackalloc EdgeSwipeSample[EdgeSwipeRecognizer.WithheldCapacity];
        Assert.Equal(0, edges.TakeWithheld(replay));
    }

    [Fact]
    public void A_claimed_gesture_allocates_nothing_a_sample()
    {
        var edges = new EdgeSwipeRecognizer();
        for (var warm = 0; warm < 4; warm++)
        {
            Drive(edges, warm);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            Drive(edges, i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        static void Drive(EdgeSwipeRecognizer edges, int seed)
        {
            var id = seed & 7;
            var start = (uint)(seed * 1000);
            edges.Begin(id, 4, 400, Width, Height, start);
            edges.Update(id, 40, 400, start + 10);
            for (var step = 0; step < 20; step++)
            {
                edges.Update(id, 40 + (step * 8), 400, start + 20 + ((uint)step * 8));
            }

            edges.End(id, start + 200);
        }
    }
}
