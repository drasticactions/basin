using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public sealed class TouchMoveResizeTests
{
    [Fact]
    public void A_grab_serial_resolves_to_the_contact_and_cancels_the_client()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        uint serial = 0;
        touch.Down += (_, e) =>
        {
            serial = e.Serial;
            log.Add($"down {e.Id}");
        };
        touch.Cancel += (_, _) => log.Add("cancel");
        host.PumpToServer();

        var router = new TouchRouter(host.Seat.Touch) { HitTester = new IdentityHitTester(window.ServerSurface) };
        var handler = new RecordingDragHandler();
        var drag = new TouchMoveResize(router, host.Seat.Touch) { Handler = handler };

        router.Down(1, 0, 100, 200);
        host.PumpUntil(() => log.Count == 1);

        Assert.True(drag.TryBegin(serial, out var x, out var y));
        Assert.Equal(100, x);
        Assert.Equal(200, y);
        Assert.True(drag.Dragging);
        host.PumpUntil(() => log.Count == 2);
        Assert.Equal(["down 0", "cancel"], log);

        router.Motion(2, 0, 150, 250);
        router.Motion(3, 0, 160, 260);
        router.Up(4, 0);

        Assert.Equal(["drag 150,250", "drag 160,260", "end false"], handler.Log);
        Assert.False(drag.Dragging);
    }

    [Fact]
    public void A_stale_serial_begins_nothing()
    {
        using var host = new CompositorTestHost();
        var router = new TouchRouter(host.Seat.Touch);
        var drag = new TouchMoveResize(router, host.Seat.Touch);

        Assert.False(drag.TryBegin(1234, out _, out _));
        Assert.False(drag.TryBegin(null, out _, out _));
        Assert.False(drag.Dragging);
    }

    [Fact]
    public void A_chrome_contact_begins_a_drag_by_id()
    {
        using var host = new CompositorTestHost();
        var chrome = new TakingChrome();
        var router = new TouchRouter(host.Seat.Touch) { Chrome = chrome };
        var handler = new RecordingDragHandler();
        var drag = new TouchMoveResize(router, host.Seat.Touch) { Handler = handler };

        router.Down(1, 5, 30, 40);
        Assert.True(drag.TryBeginContact(5, out var x, out var y));
        Assert.Equal(30, x);
        Assert.Equal(40, y);

        router.Motion(2, 5, 35, 45);
        router.Up(3, 5);

        Assert.Equal(["drag 35,45", "end false"], handler.Log);
        Assert.Equal(["press 5"], chrome.Log);
    }

    [Fact]
    public void A_touch_cancel_mid_drag_ends_cancelled()
    {
        using var host = new CompositorTestHost();
        var router = new TouchRouter(host.Seat.Touch);
        var handler = new RecordingDragHandler();
        var drag = new TouchMoveResize(router, host.Seat.Touch) { Handler = handler };

        router.Down(1, 0, 10, 10);
        Assert.True(drag.TryBeginContact(0, out _, out _));

        router.Cancel();

        Assert.Equal(["end true"], handler.Log);
        Assert.False(drag.Dragging);
    }

    private sealed class RecordingDragHandler : ITouchDragHandler
    {
        public List<string> Log { get; } = [];

        public void DragTo(double x, double y) => Log.Add($"drag {x},{y}");

        public void DragEnd(bool cancelled) => Log.Add($"end {(cancelled ? "true" : "false")}");
    }

    private sealed class TakingChrome : ITouchChrome
    {
        public List<string> Log { get; } = [];

        public bool TryPress(int id, uint timeMs, double x, double y)
        {
            Log.Add($"press {id}");
            return true;
        }

        public void Motion(int id, uint timeMs, double x, double y) => Log.Add($"motion {id}");

        public void Release(int id, uint timeMs, double x, double y) => Log.Add($"release {id}");

        public void Cancel() => Log.Add("cancel");
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
