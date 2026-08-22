using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public sealed class CentroidSwipeGestureTests
{
    [Fact]
    public void Three_fingers_claim_at_slop_and_cancel_the_client()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id}");
        touch.Motion += (_, e) => log.Add($"motion {e.Id}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        touch.Cancel += (_, _) => log.Add("cancel");
        host.PumpToServer();

        var handler = new RecordingHandler { Accept = true };
        var gesture = new CentroidSwipeGesture { Fingers = 3, Slop = 24, Handler = handler };
        var router = new TouchRouter(host.Seat.Touch)
        {
            HitTester = new IdentityHitTester(window.ServerSurface),
            Gestures = gesture,
        };

        router.Down(1, 0, 10, 10);
        router.Down(2, 1, 20, 10);
        router.Down(3, 2, 30, 10);
        router.Motion(4, 0, 40, 10);
        router.Motion(5, 1, 50, 10);
        host.PumpUntil(() => log.Count == 5);
        Assert.False(gesture.IsClaimed);

        router.Motion(6, 2, 60, 10);
        host.PumpUntil(() => log.Count == 6);

        Assert.True(gesture.IsClaimed);
        Assert.Equal("cancel", log[^1]);
        Assert.Equal(["begin 50,10"], handler.Log);

        router.Motion(7, 0, 70, 10);
        Assert.Equal(2, handler.Log.Count);
        Assert.StartsWith("update", handler.Log[1]);

        router.Up(8, 0);
        Assert.Equal("end false", handler.Log[^1]);
        Assert.False(gesture.IsClaimed);

        router.Motion(9, 1, 80, 10);
        router.Up(10, 1);
        router.Up(11, 2);
        host.PumpToClient();
        Assert.Equal(6, log.Count);

        router.Down(12, 0, 10, 10);
        host.PumpUntil(() => log.Count == 7);
        Assert.Equal("down 0", log[^1]);
    }

    [Fact]
    public void A_refused_begin_returns_the_contacts_to_the_client()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Motion += (_, e) => log.Add($"motion {e.Id}");
        touch.Cancel += (_, _) => log.Add("cancel");
        host.PumpToServer();

        var handler = new RecordingHandler { Accept = false };
        var gesture = new CentroidSwipeGesture { Fingers = 2, Slop = 10, Handler = handler };
        var router = new TouchRouter(host.Seat.Touch)
        {
            HitTester = new IdentityHitTester(window.ServerSurface),
            Gestures = gesture,
        };

        router.Down(1, 0, 10, 10);
        router.Down(2, 1, 30, 10);
        router.Motion(3, 0, 40, 10);
        host.PumpUntil(() => log.Count == 1);

        Assert.False(gesture.IsClaimed);
        Assert.Equal(["begin 35,10"], handler.Log);
        Assert.Equal(["motion 0"], log);

        router.Motion(4, 1, 60, 10);
        host.PumpUntil(() => log.Count == 2);
        Assert.Equal(["motion 0", "motion 1"], log);
        Assert.Single(handler.Log);
    }

    [Fact]
    public void Fewer_fingers_never_watch()
    {
        using var host = new CompositorTestHost();
        var handler = new RecordingHandler { Accept = true };
        var gesture = new CentroidSwipeGesture { Fingers = 3, Slop = 10, Handler = handler };
        var router = new TouchRouter(host.Seat.Touch) { Gestures = gesture };

        router.Down(1, 0, 10, 10);
        router.Down(2, 1, 20, 10);
        router.Motion(3, 0, 200, 10);
        router.Up(4, 0);
        router.Up(5, 1);

        Assert.Empty(handler.Log);
    }

    [Fact]
    public void A_cancel_mid_claim_reports_a_cancelled_end()
    {
        using var host = new CompositorTestHost();
        var handler = new RecordingHandler { Accept = true };
        var gesture = new CentroidSwipeGesture { Fingers = 2, Slop = 10, Handler = handler };
        var router = new TouchRouter(host.Seat.Touch) { Gestures = gesture };

        router.Down(1, 0, 10, 10);
        router.Down(2, 1, 30, 10);
        router.Motion(3, 0, 60, 10);
        Assert.True(gesture.IsClaimed);

        router.Cancel();
        Assert.Equal("end true", handler.Log[^1]);
        Assert.False(gesture.IsClaimed);
    }

    private sealed class RecordingHandler : ICentroidSwipeHandler
    {
        public bool Accept { get; set; }

        public List<string> Log { get; } = [];

        public bool Begin(double centroidX, double centroidY, uint timeMs)
        {
            Log.Add($"begin {centroidX},{centroidY}");
            return Accept;
        }

        public void Update(double dx, double dy, uint timeMs) => Log.Add($"update {dx},{dy}");

        public void End(bool cancelled, uint timeMs) => Log.Add($"end {(cancelled ? "true" : "false")}");
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
