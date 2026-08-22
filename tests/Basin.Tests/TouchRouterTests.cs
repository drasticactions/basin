using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public sealed class TouchRouterTests
{
    [Fact]
    public void Chrome_gets_first_refusal_and_keeps_the_contact()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = Record(touch);
        host.PumpToServer();

        var chrome = new FakeChrome { Take = true };
        var hit = new FakeHitTester { Surface = window.ServerSurface };
        var router = new TouchRouter(host.Seat.Touch) { Chrome = chrome, HitTester = hit };

        router.Down(1, 0, 10, 10);
        router.Motion(2, 0, 15, 15);
        router.Up(3, 0);
        host.PumpToClient();

        Assert.Equal(["press 0 10,10", "motion 0 15,15", "release 0 15,15"], chrome.Log);
        Assert.Empty(log);
        Assert.Equal(0, hit.HitCalls);
    }

    [Fact]
    public void A_client_contact_latches_and_maps_through_the_token()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = Record(touch);
        host.PumpToServer();

        var hit = new FakeHitTester { Surface = window.ServerSurface };
        var interactions = new FakeInteractions();
        var router = new TouchRouter(host.Seat.Touch) { HitTester = hit, Interaction = interactions };

        router.Down(1, 0, 10, 20);
        router.Motion(2, 0, 15, 25);
        router.Up(3, 0);
        host.PumpUntil(() => log.Count == 3);

        Assert.Equal(["down 0 10,20", "motion 0 15,25", "up 0"], log);
        Assert.Equal([(0, TouchTargetKind.Client)], interactions.Log);
    }

    [Fact]
    public void A_client_without_touch_drives_the_pointer()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.PumpToServer();

        var hit = new FakeHitTester { Surface = window.ServerSurface };
        var pointer = new FakePointerTarget();
        var interactions = new FakeInteractions();
        var router = new TouchRouter(host.Seat.Touch)
        {
            HitTester = hit,
            Pointer = new TouchPointerDriver(host.Seat.Touch, pointer),
            Interaction = interactions,
        };

        router.Down(1, 0, 10, 20);
        router.Motion(2, 0, 15, 25);
        router.Up(3, 0);

        Assert.Equal(["warp 10,20", "button down", "warp 15,25", "button up"], pointer.Log);
        Assert.Equal([(0, TouchTargetKind.Pointer)], interactions.Log);
    }

    [Fact]
    public void Background_contacts_drive_the_pointer_only_when_asked()
    {
        using var host = new CompositorTestHost();
        var pointer = new FakePointerTarget();
        var driver = new TouchPointerDriver(host.Seat.Touch, pointer);
        var router = new TouchRouter(host.Seat.Touch) { HitTester = new FakeHitTester(), Pointer = driver };

        router.Down(1, 0, 10, 20);
        router.Up(2, 0);
        Assert.Empty(pointer.Log);

        driver.ClaimWithoutSurface = true;
        router.Down(3, 0, 10, 20);
        router.Up(4, 0);
        Assert.Equal(["warp 10,20", "button down", "button up"], pointer.Log);
    }

    [Fact]
    public void A_gesture_claim_cancels_client_points_chrome_and_emulation()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = Record(touch);
        host.PumpToServer();

        var chrome = new FakeChrome();
        var pointer = new FakePointerTarget();
        var gestures = new ScriptedGestures();
        var driver = new TouchPointerDriver(host.Seat.Touch, pointer) { ClaimWithoutSurface = true };
        var router = new TouchRouter(host.Seat.Touch)
        {
            Chrome = chrome,
            HitTester = new FakeHitTester { Surface = window.ServerSurface, Region = 100 },
            Pointer = driver,
            Gestures = gestures,
        };

        router.Down(1, 0, 10, 10);
        router.Down(2, 1, 200, 200);
        host.PumpUntil(() => log.Count == 1);
        Assert.Equal(["down 0 10,10"], log);
        Assert.True(driver.Owns(1));

        gestures.Verdict = TouchGestureVerdict.Claim;
        router.Motion(3, 0, 40, 10);
        gestures.Verdict = TouchGestureVerdict.Pass;
        host.PumpUntil(() => log.Count == 2);

        Assert.Equal(["down 0 10,10", "cancel"], log);
        Assert.False(driver.Active);
        Assert.Contains("button up", pointer.Log);
        Assert.Contains("cancel", chrome.Log);

        var pointerEvents = pointer.Log.Count;
        router.Motion(4, 1, 210, 210);
        router.Up(5, 1);
        Assert.Equal(pointerEvents, pointer.Log.Count);

        router.Up(6, 0);
        host.PumpToClient();
        Assert.Equal(["down 0 10,10", "cancel"], log);
    }

    [Fact]
    public void A_decline_replays_the_withheld_events_in_order()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = Record(touch);
        host.PumpToServer();

        var gestures = new ScriptedGestures { Verdict = TouchGestureVerdict.Withhold, Withholding = true };
        var router = new TouchRouter(host.Seat.Touch)
        {
            HitTester = new FakeHitTester { Surface = window.ServerSurface },
            Gestures = gestures,
        };

        router.Down(1, 0, 10, 10);
        router.Motion(2, 0, 12, 12);
        router.Motion(3, 0, 14, 14);
        host.PumpToClient();
        Assert.Empty(log);

        gestures.Verdict = TouchGestureVerdict.Decline;
        gestures.Withholding = false;
        router.Motion(4, 0, 16, 16);
        router.Up(5, 0);
        host.PumpUntil(() => log.Count == 5);

        Assert.Equal(
            ["down 0 10,10", "motion 0 12,12", "motion 0 14,14", "motion 0 16,16", "up 0"], log);
    }

    [Fact]
    public void A_withheld_up_replays_and_ends_the_sequence()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = Record(touch);
        host.PumpToServer();

        var gestures = new ScriptedGestures { Verdict = TouchGestureVerdict.Withhold, Withholding = true };
        var router = new TouchRouter(host.Seat.Touch)
        {
            HitTester = new FakeHitTester { Surface = window.ServerSurface },
            Gestures = gestures,
        };

        router.Down(1, 0, 10, 10);
        gestures.Verdict = TouchGestureVerdict.Decline;
        gestures.Withholding = false;
        router.Up(2, 0);
        host.PumpUntil(() => log.Count == 2);

        Assert.Equal(["down 0 10,10", "up 0"], log);
        Assert.False(host.Seat.Touch.HasPoints);
    }

    [Fact]
    public void A_dead_client_target_becomes_a_local_cancel()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var touch = client.Seat!.GetTouch();
        var log = Record(touch);
        host.PumpToServer();

        var hit = new FakeHitTester { Surface = window.ServerSurface };
        var router = new TouchRouter(host.Seat.Touch) { HitTester = hit };

        router.Down(1, 0, 10, 10);
        hit.MapValid = false;
        router.Motion(2, 0, 15, 15);

        Assert.False(host.Seat.Touch.HasPoints);

        router.Motion(3, 0, 20, 20);
        router.Up(4, 0);
        host.PumpUntil(() => log.Count >= 2);
        Assert.Equal(["down 0 10,10", "up 0"], log);
    }

    [Fact]
    public void Synthetic_contacts_reach_recognisers_and_never_delivery()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.PumpToServer();

        var gestures = new ScriptedGestures();
        var hit = new FakeHitTester { Surface = window.ServerSurface };
        var router = new TouchRouter(host.Seat.Touch) { HitTester = hit, Gestures = gestures };

        Assert.Equal(TouchGestureVerdict.Pass, router.SyntheticDown(-2, 1, 10, 10));
        Assert.Equal(TouchGestureVerdict.Pass, router.SyntheticMotion(-2, 2, 15, 15));
        Assert.Equal(TouchGestureVerdict.Pass, router.SyntheticUp(-2, 3));

        Assert.Equal(["down -2", "motion -2", "up -2"], gestures.Log);
        Assert.Equal(0, hit.HitCalls);
        Assert.False(host.Seat.Touch.HasPoints);
        Assert.Throws<ArgumentOutOfRangeException>(() => router.SyntheticDown(0, 4, 1, 1));
    }

    [Fact]
    public void Activity_reports_on_down_motion_up_and_cancel_never_frame()
    {
        using var host = new CompositorTestHost();
        var idle = new SeatIdleSource();
        var count = 0;
        idle.Activity += () => count++;
        var router = new TouchRouter(host.Seat.Touch) { Activity = idle };

        router.Down(1, 0, 10, 10);
        router.Motion(2, 0, 15, 15);
        router.Frame();
        router.Up(3, 0);
        router.Cancel();

        Assert.Equal(4, count);
    }

    private static List<string> Record(Wayland.WlTouch touch)
    {
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Motion += (_, e) => log.Add($"motion {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        touch.Cancel += (_, _) => log.Add("cancel");
        return log;
    }

    private sealed class FakeHitTester : ITouchHitTester
    {
        private readonly object _token = new();

        public Basin.Surface? Surface { get; set; }

        public double Region { get; set; } = double.MaxValue;

        public bool MapValid { get; set; } = true;

        public int HitCalls { get; private set; }

        public bool TryHit(double layoutX, double layoutY, out TouchHit hit)
        {
            HitCalls++;
            if (Surface is null || layoutX >= Region)
            {
                hit = default;
                return false;
            }

            hit = new TouchHit(Surface, layoutX, layoutY, _token);
            return true;
        }

        public bool TryMap(object? token, double layoutX, double layoutY, out double localX, out double localY)
        {
            localX = layoutX;
            localY = layoutY;
            return MapValid && ReferenceEquals(token, _token);
        }
    }

    private sealed class FakeChrome : ITouchChrome
    {
        public bool Take { get; set; }

        public List<string> Log { get; } = [];

        public bool TryPress(int id, uint timeMs, double x, double y)
        {
            if (!Take)
            {
                return false;
            }

            Log.Add($"press {id} {x},{y}");
            return true;
        }

        public void Motion(int id, uint timeMs, double x, double y) => Log.Add($"motion {id} {x},{y}");

        public void Release(int id, uint timeMs, double x, double y) => Log.Add($"release {id} {x},{y}");

        public void Cancel() => Log.Add("cancel");
    }

    private sealed class FakePointerTarget : ITouchPointerTarget
    {
        public List<string> Log { get; } = [];

        public void Warp(uint timeMs, double x, double y) => Log.Add($"warp {x},{y}");

        public void Button(uint timeMs, uint button, bool pressed) =>
            Log.Add(pressed ? "button down" : "button up");
    }

    private sealed class FakeInteractions : ITouchInteractionObserver
    {
        public List<(int Id, TouchTargetKind Kind)> Log { get; } = [];

        public void OnTouchInteraction(int id, TouchTargetKind kind, Basin.Surface? surface) =>
            Log.Add((id, kind));
    }

    private sealed class ScriptedGestures : ITouchGestures
    {
        private readonly EdgeSwipeSample[] _withheld = new EdgeSwipeSample[EdgeSwipeRecognizer.WithheldCapacity];
        private int _count;

        public TouchGestureVerdict Verdict { get; set; } = TouchGestureVerdict.Pass;

        public bool Withholding { get; set; }

        public List<string> Log { get; } = [];

        public TouchGestureVerdict Down(int id, uint timeMs, double x, double y)
        {
            Log.Add($"down {id}");
            if (Withholding)
            {
                _withheld[_count++] = new EdgeSwipeSample(timeMs, x, y, Down: true);
            }

            return Verdict;
        }

        public TouchGestureVerdict Motion(int id, uint timeMs, double x, double y)
        {
            Log.Add($"motion {id}");
            if (Withholding)
            {
                _withheld[_count++] = new EdgeSwipeSample(timeMs, x, y, Down: false);
            }

            return Verdict;
        }

        public TouchGestureVerdict Up(int id, uint timeMs)
        {
            Log.Add($"up {id}");
            return Verdict;
        }

        public void Cancel() => Log.Add("cancel");

        public int TakeWithheld(Span<EdgeSwipeSample> into)
        {
            var count = Math.Min(_count, into.Length);
            for (var i = 0; i < count; i++)
            {
                into[i] = _withheld[i];
            }

            _count = 0;
            return count;
        }
    }
}
