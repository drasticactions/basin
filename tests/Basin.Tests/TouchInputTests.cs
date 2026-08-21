using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class TouchInputTests
{
    [Fact]
    public void Touch_round_trip_ends_every_group_with_a_frame()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        uint downSerial = 0;
        touch.Down += (_, e) =>
        {
            downSerial = e.Serial;
            log.Add($"down {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        };
        touch.Motion += (_, e) => log.Add($"motion {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        touch.Frame += (_, _) => log.Add("frame");
        host.PumpToClient();

        var serverDownSerial = host.Seat.Touch.NotifyDown(window.ServerSurface, 1, 0, 10, 20);
        host.Seat.Touch.NotifyFrame();
        Assert.True(host.Seat.Touch.HasPoints);

        Assert.True(host.Seat.ValidateGrabSerial(serverDownSerial));
        Assert.True(host.Seat.ValidateImplicitGrabSerial(serverDownSerial));

        host.Seat.Touch.NotifyMotion(2, 0, 15, 25);
        host.Seat.Touch.NotifyFrame();

        host.Seat.Touch.NotifyUp(3, 0);
        host.Seat.Touch.NotifyFrame();
        Assert.False(host.Seat.Touch.HasPoints);
        Assert.False(host.Seat.ValidateImplicitGrabSerial(serverDownSerial));
        Assert.True(host.Seat.ValidateGrabSerial(serverDownSerial));

        host.PumpUntil(() => log.Count == 6);
        Assert.Equal(["down 0 10,20", "frame", "motion 0 15,25", "frame", "up 0", "frame"], log);
        Assert.Equal(serverDownSerial, downSerial);
    }

    [Fact]
    public void Touch_cancel_discards_all_points_without_a_frame()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        touch.Frame += (_, _) => log.Add("frame");
        touch.Cancel += (_, _) => log.Add("cancel");
        host.PumpToClient();

        host.Seat.Touch.NotifyDown(window.ServerSurface, 1, 0, 10, 20);
        host.Seat.Touch.NotifyDown(window.ServerSurface, 1, 1, 30, 40);
        host.Seat.Touch.NotifyFrame();
        host.PumpUntil(() => log.Count == 3);

        host.Seat.Touch.NotifyCancel();
        Assert.False(host.Seat.Touch.HasPoints);

        host.Seat.Touch.NotifyFrame();
        host.Seat.Touch.NotifyUp(2, 0);
        host.Seat.Touch.NotifyFrame();
        host.PumpUntil(() => log.Count == 4);
        host.PumpToClient();
        Assert.Equal(["down 0", "down 1", "frame", "cancel"], log);
    }

    [Fact]
    public void Seat_capability_changes_reach_bound_clients()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        WlSeat.Capability? capabilities = null;
        client.Seat!.Capabilities += (_, e) => capabilities = e.Capabilities;

        host.Seat.Capabilities &= ~Basin.Seat.SeatCapability.Touch;
        host.PumpUntil(() => capabilities is not null);
        Assert.False(capabilities!.Value.HasFlag(WlSeat.Capability.Touch));

        capabilities = null;
        host.Seat.Capabilities |= Basin.Seat.SeatCapability.Touch;
        host.PumpUntil(() => capabilities is not null);
        Assert.True(capabilities!.Value.HasFlag(WlSeat.Capability.Touch));
    }

    [Fact]
    public void Touch_grab_intercepts_delivery_until_ended()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        touch.Frame += (_, _) => log.Add("frame");
        host.PumpToClient();

        var grab = new RecordingTouchGrab();
        host.Seat.Touch.StartGrab(grab);
        host.Seat.Touch.NotifyDown(window.ServerSurface, 1, 0, 5, 5);
        host.Seat.Touch.NotifyMotion(2, 0, 6, 6);
        host.Seat.Touch.NotifyUp(3, 0);
        host.Seat.Touch.NotifyFrame();
        host.PumpToClient();

        Assert.Equal(["down 0", "motion 0", "up 0", "frame"], grab.Calls);
        Assert.Empty(log);
        Assert.False(host.Seat.Touch.HasPoints);

        host.Seat.Touch.EndGrab(grab);
        host.Seat.Touch.NotifyDown(window.ServerSurface, 4, 0, 5, 5);
        host.Seat.Touch.NotifyUp(5, 0);
        host.Seat.Touch.NotifyFrame();
        host.PumpUntil(() => log.Count == 3);
        Assert.Equal(["down 0", "up 0", "frame"], log);
    }

    [Fact]
    public void Popup_dismisses_on_touch_outside()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var parent = MappedToplevel.Map(host, client, 100, 80);

        var touch = client.Seat!.GetTouch();
        uint downSerial = 0;
        touch.Down += (_, e) => downSerial = e.Serial;
        host.PumpToClient();
        host.Seat.Touch.NotifyDown(parent.ServerSurface, 1, 0, 15, 15);
        host.Seat.Touch.NotifyUp(2, 0);
        host.Seat.Touch.NotifyFrame();
        host.PumpUntil(() => downSerial != 0);

        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(50, 40);
        positioner.SetAnchorRect(10, 10, 20, 20);
        positioner.SetAnchor(Basin.Shell.Xdg.Protocol.XdgPositioner.Anchor.BottomRight);
        positioner.SetGravity(Basin.Shell.Xdg.Protocol.XdgPositioner.Gravity.BottomRight);

        Basin.Shell.Xdg.XdgPopupWindow? serverPopup = null;
        host.Shell.NewPopup += popup => serverPopup = popup;

        var popupSurface = client.Compositor.CreateSurface();
        var popupXdg = client.WmBase.GetXdgSurface(popupSurface);
        var popup = popupXdg.GetPopup(parent.XdgSurface, positioner);
        popup.Grab(client.Seat, downSerial);
        var popupDone = false;
        popup.PopupDone += (_, _) => popupDone = true;
        var configured = false;
        popupXdg.Configure += (_, e) =>
        {
            popupXdg.AckConfigure(e.Serial);
            configured = true;
        };
        popupSurface.Commit();
        host.PumpUntil(() => configured);
        Assert.NotNull(serverPopup);
        Assert.True(serverPopup!.HasGrab);

        var popupBuffer = client.CreateBuffer(50, 40, Fill.Solid(50, 40, 0xFF444444));
        popupSurface.Attach(popupBuffer.Proxy, 0, 0);
        popupSurface.Commit();
        host.PumpToServer();

        var outsider = host.ConnectClient();
        var other = MappedToplevel.Map(host, outsider);
        host.Seat.Touch.NotifyDown(other.ServerSurface, 3, 0, 5, 5);
        host.PumpUntil(() => popupDone);
    }

    [Fact]
    public void Headless_touchscreen_drives_the_seat_through_the_scene()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        var sceneSurface = host.SurfaceScenes.Single(s => s.Surface == window.ServerSurface);
        sceneSurface.Tree.SetPosition(20, 10);

        var touch = client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Motion += (_, e) => log.Add($"motion {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        touch.Frame += (_, _) => log.Add("frame");
        host.PumpToClient();

        var screen = host.Backend.CreateTouchScreen();
        var latched = new Dictionary<int, SceneNode>();
        (double X, double Y) Layout(double nx, double ny) => (nx * 160, ny * 120);
        screen.Down += (time, slot, nx, ny) =>
        {
            var (x, y) = Layout(nx, ny);
            if (host.Scene.SurfaceAt(x, y) is { Surface: { } surface } hit)
            {
                latched[slot] = hit.Node;
                host.Seat.Touch.NotifyDown(surface, time, slot, hit.X, hit.Y);
            }
        };
        screen.Motion += (time, slot, nx, ny) =>
        {
            if (latched.TryGetValue(slot, out var node))
            {
                var (x, y) = Layout(nx, ny);
                double nodeX = 0, nodeY = 0;
                for (SceneNode? n = node; n is not null; n = n.Parent)
                {
                    nodeX += n.X;
                    nodeY += n.Y;
                }

                host.Seat.Touch.NotifyMotion(time, slot, x - nodeX, y - nodeY);
            }
        };
        screen.Up += (time, slot) =>
        {
            latched.Remove(slot);
            host.Seat.Touch.NotifyUp(time, slot);
        };
        screen.Frame += () => host.Seat.Touch.NotifyFrame();

        screen.InjectDown(1, 0, 30.0 / 160, 30.0 / 120);
        screen.InjectFrame();
        screen.InjectMotion(2, 0, 40.0 / 160, 40.0 / 120);
        screen.InjectFrame();
        screen.InjectUp(3, 0);
        screen.InjectFrame();
        screen.InjectDown(4, 0, 0.05, 0.05);
        screen.InjectFrame();

        host.PumpUntil(() => log.Count == 6);
        host.PumpToClient();
        Assert.Equal(["down 0 10,20", "frame", "motion 0 20,30", "frame", "up 0", "frame"], log);
    }

    [Fact]
    public void Accepts_reports_whether_the_point_has_anywhere_to_go()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        host.PumpToServer();

        Assert.False(host.Seat.Touch.Accepts(window.ServerSurface));
        Assert.False(host.Seat.Touch.Accepts(null));

        var touch = client.Seat!.GetTouch();
        host.PumpToServer();
        Assert.True(host.Seat.Touch.Accepts(window.ServerSurface));

        touch.Release();
        host.PumpToServer();
        Assert.False(host.Seat.Touch.Accepts(window.ServerSurface));
    }

    private sealed class SwipeGate(CompositorTestHost host)
    {
        private const double Slop = 24;

        private readonly Basin.Seat.TouchContacts _contacts = new();
        private double _travel;
        private bool _watching;
        private bool _claimed;

        public int Claims { get; private set; }

        public void Down(int id, double x, double y)
        {
            _contacts.Down(id, x, y);
            if (_claimed)
            {
                return;
            }

            if (_contacts.Count == 3)
            {
                _watching = true;
                _travel = 0;
            }
        }

        public bool Motion(int id, double x, double y)
        {
            if (!_contacts.Motion(id, x, y, out var dx, out _))
            {
                return _claimed;
            }

            if (_claimed)
            {
                return true;
            }

            if (!_watching)
            {
                return false;
            }

            _travel += dx;
            if (Math.Abs(_travel) < Slop)
            {
                return false;
            }

            _claimed = true;
            _watching = false;
            Claims++;
            host.Seat.Touch.NotifyCancel();
            return true;
        }

        public bool Up(int id)
        {
            _contacts.Up(id);
            if (!_claimed)
            {
                _watching = _contacts.Count == 3;
                return false;
            }

            _claimed = _contacts.Count > 0;
            return true;
        }
    }

    private static (SwipeGate Gate, List<string> Log, Basin.Backend.Headless.HeadlessTouchScreen Screen) SwipeHarness(
        CompositorTestHost host, int contacts)
    {
        var touch = host.Client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id}");
        touch.Motion += (_, e) => log.Add($"motion {e.Id}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        touch.Cancel += (_, _) => log.Add("cancel");
        host.PumpToClient();

        var gate = new SwipeGate(host);
        var screen = host.Backend.CreateTouchScreen();
        (double X, double Y) Layout(double nx, double ny) => (nx * 160, ny * 120);
        screen.Down += (time, slot, nx, ny) =>
        {
            var (x, y) = Layout(nx, ny);
            gate.Down(slot, x, y);
            if (host.Scene.SurfaceAt(x, y) is { Surface: { } surface } hit)
            {
                host.Seat.Touch.NotifyDown(surface, time, slot, hit.X, hit.Y);
            }
        };
        screen.Motion += (time, slot, nx, ny) =>
        {
            var (x, y) = Layout(nx, ny);
            if (!gate.Motion(slot, x, y))
            {
                host.Seat.Touch.NotifyMotion(time, slot, x, y);
            }
        };
        screen.Up += (time, slot) =>
        {
            if (!gate.Up(slot))
            {
                host.Seat.Touch.NotifyUp(time, slot);
            }
        };
        screen.Frame += () => host.Seat.Touch.NotifyFrame();

        for (var i = 0; i < contacts; i++)
        {
            screen.InjectDown(1, i, (20.0 + (i * 10)) / 160, 30.0 / 120);
        }

        screen.InjectFrame();
        return (gate, log, screen);
    }

    private static void Sweep(Basin.Backend.Headless.HeadlessTouchScreen screen, int contacts, double pixels)
    {
        for (var step = 1; step <= 4; step++)
        {
            for (var i = 0; i < contacts; i++)
            {
                screen.InjectMotion((uint)(1 + step), i, (20.0 + (i * 10) + (pixels * step / 4)) / 160, 30.0 / 120);
            }

            screen.InjectFrame();
        }
    }

    [Fact]
    public void Three_contacts_past_the_threshold_cancel_the_client_once()
    {
        using var host = new CompositorTestHost();
        _ = MappedToplevel.Map(host, host.Client);
        var (gate, log, screen) = SwipeHarness(host, contacts: 3);

        Sweep(screen, 3, 40);

        host.PumpToClient();
        Assert.Equal(1, gate.Claims);
        Assert.Equal(1, log.Count(l => l == "cancel"));
        Assert.DoesNotContain("motion", log[(log.IndexOf("cancel") + 1)..]);
    }

    [Fact]
    public void Two_contacts_are_never_claimed()
    {
        using var host = new CompositorTestHost();
        _ = MappedToplevel.Map(host, host.Client);
        var (gate, log, screen) = SwipeHarness(host, contacts: 2);

        Sweep(screen, 2, 40);

        host.PumpToClient();
        Assert.Equal(0, gate.Claims);
        Assert.DoesNotContain("cancel", log);
        Assert.Contains("motion 0", log);
    }

    [Fact]
    public void Three_resting_contacts_are_never_claimed()
    {
        using var host = new CompositorTestHost();
        _ = MappedToplevel.Map(host, host.Client);
        var (gate, log, screen) = SwipeHarness(host, contacts: 3);

        Sweep(screen, 3, 8);

        host.PumpToClient();
        Assert.Equal(0, gate.Claims);
        Assert.DoesNotContain("cancel", log);
    }

    [Fact]
    public void A_contact_landing_mid_sweep_does_not_move_the_centre()
    {
        using var host = new CompositorTestHost();
        _ = MappedToplevel.Map(host, host.Client);
        var (gate, _, screen) = SwipeHarness(host, contacts: 3);
        Sweep(screen, 3, 8);

        screen.InjectDown(9, 3, 150.0 / 160, 30.0 / 120);
        screen.InjectFrame();
        Sweep(screen, 3, 8);

        Assert.Equal(0, gate.Claims);
    }

    private sealed class RecordingTouchGrab : Basin.Seat.ITouchGrab
    {
        public List<string> Calls { get; } = [];

        public uint Down(Surface surface, uint timeMs, int id, double x, double y)
        {
            Calls.Add($"down {id}");
            return 0;
        }

        public void Up(uint timeMs, int id) => Calls.Add($"up {id}");

        public void Motion(uint timeMs, int id, double x, double y) => Calls.Add($"motion {id}");

        public void Frame() => Calls.Add("frame");

        public void Cancel() => Calls.Add("cancel");
    }
}
