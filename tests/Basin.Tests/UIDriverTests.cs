using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Scene;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed class UIDriverTests : IDisposable
{
    private readonly WlServerDisplay _display;
    private readonly WaylandEventLoop _loop;
    private readonly Scene.Scene _scene = new();

    public UIDriverTests()
    {
        CompositorTestHost.SkipWithoutWaylandServer();
        BasinCounters.Reset();
        _display = CompositorTestHost.TransportUnderTest == Basin.Cli.TransportKind.Managed
            ? WlServerDisplay.Create(new ManagedTransport())
            : WlServerDisplay.Create();
        _loop = new WaylandEventLoop(_display);
    }

    public void Dispose()
    {
        _scene.Root.Destroy();
        _display.Dispose();
    }

    [Fact]
    public void Pumping_syncs_popups_and_reschedules_from_the_host()
    {
        using var host = new PopupUIHost();
        using var driver = new UIDriver(host, _loop) { PopupLayer = _scene.Root };
        driver.Start();

        var popup = host.OpenPopup(12.4, 30.6);
        Assert.Single(driver.Popups);
        Assert.Equal(12, driver.Popups[0].Node.X);
        Assert.Equal(31, driver.Popups[0].Node.Y);

        popup.SetPosition(80.2, 90.9);
        driver.Pump();
        Assert.Equal(1, host.Pumps);
        Assert.Equal(80, driver.Popups[0].Node.X);
        Assert.Equal(91, driver.Popups[0].Node.Y);

        host.ClosePopup(popup);
        Assert.Empty(driver.Popups);
    }

    [Fact]
    public void A_wakeup_request_moves_the_timer_to_what_the_host_is_due()
    {
        using var host = new PopupUIHost { Due = 5 };
        using var driver = new UIDriver(host, _loop);
        var woken = 0;
        driver.Woken += () => woken++;
        driver.Start();

        host.RequestWakeup();
        _loop.Dispatch(20);
        Assert.True(woken >= 1);
        Assert.True(host.Pumps >= 1);
    }

    [Fact]
    public void Disposal_drops_every_popup_and_stops_listening()
    {
        using var host = new PopupUIHost();
        var driver = new UIDriver(host, _loop) { PopupLayer = _scene.Root };
        driver.Start();
        host.OpenPopup(0, 0);
        Assert.Single(driver.Popups);

        driver.Dispose();
        Assert.Empty(driver.Popups);

        host.OpenPopup(4, 4);
        Assert.Empty(driver.Popups);
    }

    private sealed class PopupUIHost : IUIHost
    {
        private readonly List<FalsifierUISurface> _popups = [];
        private readonly FalsifierUIHost _inner = new();

        public int Pumps { get; private set; }

        public long? Due { get; set; }

        public UITargetKind Produces => UITargetKind.Memory;

        public long? NextDueMillis => Due;

        public event Action? WakeupRequested;

        public event Action<IUISurface>? PopupAppeared;

        public event Action<IUISurface>? PopupDismissed;

        public IUISurface? CreateSurface(in UISurfaceOptions options) => _inner.CreateSurface(options);

        public void Pump() => Pumps++;

        public void RequestWakeup() => WakeupRequested?.Invoke();

        public PlacedUISurface OpenPopup(double x, double y)
        {
            var surface = (FalsifierUISurface)_inner.CreateSurface(new UISurfaceOptions
            {
                Target = UITargetKind.Memory,
                Width = 40,
                Height = 20,
                Scale = 1.0,
            })!;
            _popups.Add(surface);
            var placed = new PlacedUISurface(surface, x, y);
            PopupAppeared?.Invoke(placed);
            return placed;
        }

        public void ClosePopup(PlacedUISurface popup) => PopupDismissed?.Invoke(popup);

        public void Dispose()
        {
            foreach (var popup in _popups)
            {
                popup.Dispose();
            }

            _popups.Clear();
            _inner.Dispose();
        }
    }

    private sealed class PlacedUISurface : IUISurface
    {
        private readonly FalsifierUISurface _inner;

        public PlacedUISurface(FalsifierUISurface inner, double x, double y)
        {
            _inner = inner;
            PositionX = x;
            PositionY = y;
        }

        public double PositionX { get; private set; }

        public double PositionY { get; private set; }

        public UISurfaceSize Size => _inner.Size;

        public void SetPosition(double x, double y)
        {
            PositionX = x;
            PositionY = y;
        }

        public bool Configure(int logicalWidth, int logicalHeight, double scale) =>
            _inner.Configure(logicalWidth, logicalHeight, scale);

        public bool TryAcquire(out UIFrame frame) => _inner.TryAcquire(out frame);

        public void AddObserver(IUISurfaceObserver observer)
        {
        }

        public void RemoveObserver(IUISurfaceObserver observer)
        {
        }

        public bool AcceptsInputAt(double x, double y) => _inner.AcceptsInputAt(x, y);

        public string? CursorAt(double x, double y) => null;

        public void NotifyPointerEnter(double x, double y)
        {
        }

        public void NotifyPointerMotion(uint timeMs, double x, double y)
        {
        }

        public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
        {
        }

        public void NotifyPointerAxis(uint timeMs, double dx, double dy)
        {
        }

        public void NotifyPointerLeave()
        {
        }

        public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

        public void Dispose()
        {
        }
    }
}
