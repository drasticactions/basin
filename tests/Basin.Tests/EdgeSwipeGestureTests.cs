using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public sealed class EdgeSwipeGestureTests
{
    [Fact]
    public void An_edge_contact_withholds_claims_and_finishes()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id}");
        touch.Motion += (_, e) => log.Add($"motion {e.Id}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        host.PumpToServer();

        var handler = new RecordingHandler(new EdgeSwipeArea(0, 0, 800, 600));
        var gesture = new EdgeSwipeGesture { Handler = handler };
        var router = new TouchRouter(host.Seat.Touch)
        {
            HitTester = new IdentityHitTester(window.ServerSurface),
            Gestures = gesture,
        };

        router.Down(1, 0, 5, 300);
        Assert.True(gesture.IsActive);

        router.Motion(2, 0, 20, 300);
        Assert.Equal(ScreenEdge.Left, gesture.Recognizer.Edge);
        Assert.Equal(["claimed left"], handler.Log);

        router.Motion(3, 0, 100, 300);
        Assert.Equal(["claimed left", "track left"], handler.Log);

        router.Up(4, 0);
        Assert.Equal("finished In", handler.Log[^1]);
        Assert.False(gesture.IsActive);

        host.PumpToClient();
        Assert.Empty(log);
    }

    [Fact]
    public void A_sideways_drift_declines_and_replays_to_the_client()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Motion += (_, e) => log.Add($"motion {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        host.PumpToServer();

        var handler = new RecordingHandler(new EdgeSwipeArea(0, 0, 800, 600));
        var gesture = new EdgeSwipeGesture { Handler = handler };
        var router = new TouchRouter(host.Seat.Touch)
        {
            HitTester = new IdentityHitTester(window.ServerSurface),
            Gestures = gesture,
        };

        router.Down(1, 0, 5, 300);
        host.PumpToClient();
        Assert.Empty(log);

        router.Motion(2, 0, 6, 340);
        router.Up(3, 0);
        host.PumpUntil(() => log.Count == 3);

        Assert.Equal(["down 0 5,300", "motion 0 6,340", "up 0"], log);
        Assert.Empty(handler.Log);
        Assert.False(gesture.IsActive);
    }

    [Fact]
    public void A_withheld_release_replays_through_an_offset_area()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Motion += (_, e) => log.Add($"motion {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        host.PumpToServer();

        var handler = new RecordingHandler(new EdgeSwipeArea(1000, 0, 800, 600));
        var gesture = new EdgeSwipeGesture { Handler = handler };
        var router = new TouchRouter(host.Seat.Touch)
        {
            HitTester = new IdentityHitTester(window.ServerSurface),
            Gestures = gesture,
        };

        router.Down(1, 0, 1005, 300);
        host.PumpToClient();
        Assert.Empty(log);

        router.Up(2, 0);
        host.PumpUntil(() => log.Count == 3);

        Assert.Equal(["down 0 1005,300", "motion 0 1005,300", "up 0"], log);
        Assert.False(gesture.IsActive);
    }

    [Fact]
    public void A_contact_outside_every_band_flows_through()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id}");
        host.PumpToServer();

        var handler = new RecordingHandler(new EdgeSwipeArea(0, 0, 800, 600));
        var gesture = new EdgeSwipeGesture { Handler = handler };
        var router = new TouchRouter(host.Seat.Touch)
        {
            HitTester = new IdentityHitTester(window.ServerSurface),
            Gestures = gesture,
        };

        router.Down(1, 0, 400, 300);
        host.PumpUntil(() => log.Count == 1);

        Assert.False(gesture.IsActive);
        Assert.Equal(["down 0"], log);
    }

    private sealed class RecordingHandler : IEdgeSwipeHandler
    {
        private readonly EdgeSwipeArea _area;

        public RecordingHandler(EdgeSwipeArea area) => _area = area;

        public List<string> Log { get; } = [];

        public bool TryArea(double layoutX, double layoutY, out EdgeSwipeArea area)
        {
            area = _area;
            return layoutX >= _area.X && layoutX < _area.X + _area.Width;
        }

        public void Claimed(EdgeSwipeRecognizer recognizer) =>
            Log.Add($"claimed {recognizer.Edge.ToString().ToLowerInvariant()}");

        public void Track(EdgeSwipeRecognizer recognizer) =>
            Log.Add($"track {recognizer.Edge.ToString().ToLowerInvariant()}");

        public void Finished(EdgeSwipeRecognizer recognizer) =>
            Log.Add($"finished {recognizer.Outcome}");
    }

    private sealed class IdentityHitTester : ITouchHitTester
    {
        private readonly Basin.Surface _surface;
        private readonly object _token = new();

        public IdentityHitTester(Basin.Surface surface) => _surface = surface;

        public bool TryHit(double layoutX, double layoutY, out TouchHit hit)
        {
            hit = new TouchHit(_surface, layoutX, layoutY, _token);
            return true;
        }

        public bool TryMap(object? token, double layoutX, double layoutY, out double localX, out double localY)
        {
            localX = layoutX;
            localY = layoutY;
            return ReferenceEquals(token, _token);
        }
    }
}
