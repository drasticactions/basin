using Basin.Capabilities;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Greetd.Host;
using Basin.Host;
using Basin.Scene;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class ChromeSeatTests
{
    [Fact]
    public void Touch_and_keys_reach_the_chrome_and_the_key_hook_can_take_a_key()
    {
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var layout = new OutputLayout();
        using var services = host.CreateServices().Use(layout);
        services.Install(new ProtocolPack([new CompositorModule(), new Basin.Seat.SeatModule()]));
        services.Freeze();
        var seatState = services.Require<Basin.Seat.Seat>();
        seatState.Keyboard.SetKeymap();
        var scene = new Basin.Scene.Scene();
        var index = new UISurfaceIndex();
        using var cursor = new CursorController(layout);
        var surface = new RecordingUISurface(100, 80);
        var node = new UISurfaceNode(scene.Root, surface, index);
        node.SetPosition(10, 20);
        node.Publish();
        var touched = new List<string>();
        var hooked = new List<uint>();
        using var seat = new ChromeSeat(host, seatState, layout, scene, index, cursor, null, BasinLog.For("test"))
        {
            Touched = (x, y) => touched.Add($"{x},{y}"),
            KeyHook = (_, key, pressed) =>
            {
                hooked.Add(key);
                return key == 1;
            },
        };

        Assert.True(seat.Injector.TouchDown(1, 7, 30, 40));
        Assert.True(seat.Injector.TouchUp(2, 7));
        Assert.Equal(["30,40"], touched);
        Assert.Contains("touch-down 7 20,20", surface.Log);
        Assert.Contains("touch-up 7", surface.Log);

        seat.Router.SetKeyboardFocus(surface);
        seat.Injector.Key(3, 30, pressed: true);
        seat.Injector.Key(4, 30, pressed: false);
        seat.Injector.Key(5, 1, pressed: true);
        seat.Injector.Key(6, 1, pressed: false);
        Assert.Equal([30u, 30, 1, 1], hooked);
        Assert.Contains("key 30", surface.Log);
        Assert.DoesNotContain("key 1", surface.Log);

        node.Dispose();
        surface.Dispose();
        scene.Root.Destroy();
    }

    private sealed class RecordingUISurface : IUISurface
    {
        private readonly UISurfaceObservers _observers = new();
        private readonly MemoryBuffer _buffer;
        private readonly int _width;
        private readonly int _height;
        private bool _disposed;

        public RecordingUISurface(int width, int height)
        {
            _width = width;
            _height = height;
            _buffer = new MemoryBuffer(width, height, DrmFormat.Argb8888);
        }

        public List<string> Log { get; } = [];

        public bool TextInput { get; set; }

        public UISurfaceSize Size => new(_width, _height, 1.0);

        public bool WantsTextInput => TextInput;

        public bool Configure(int logicalWidth, int logicalHeight, double scale) => true;

        public bool TryAcquire(out UIFrame frame)
        {
            if (_disposed)
            {
                frame = default;
                return false;
            }

            frame = new UIFrame(_buffer.Lock(), damage: null);
            return true;
        }

        public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

        public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

        public bool AcceptsInputAt(double x, double y) =>
            !_disposed && x >= 0 && y >= 0 && x < _width && y < _height;

        public string? CursorAt(double x, double y) => "left_ptr";

        public void NotifyPointerEnter(double x, double y) => Log.Add($"enter {x},{y}");

        public void NotifyPointerMotion(uint timeMs, double x, double y) => Log.Add($"motion {x},{y}");

        public void NotifyPointerButton(uint timeMs, uint button, bool pressed) =>
            Log.Add($"button {button} {(pressed ? "down" : "up")}");

        public void NotifyPointerAxis(uint timeMs, double dx, double dy) => Log.Add($"axis {dx},{dy}");

        public void NotifyPointerLeave() => Log.Add("leave");

        public void NotifyKeyboardEnter(ReadOnlySpan<uint> pressed)
        {
            var keys = pressed.Length == 0 ? string.Empty : $" {pressed[0]}";
            Log.Add($"kbd-enter{keys}");
        }

        public void NotifyKey(uint timeMs, uint key, bool pressed) => Log.Add($"key {key}");

        public void NotifyModifiers(uint depressed, uint latched, uint locked, uint group) =>
            Log.Add($"mods {depressed}");

        public void NotifyKeyboardLeave() => Log.Add("kbd-leave");

        public void NotifyTouchDown(uint timeMs, int id, double x, double y) =>
            Log.Add($"touch-down {id} {x},{y}");

        public void NotifyTouchMotion(uint timeMs, int id, double x, double y) =>
            Log.Add($"touch-motion {id} {x},{y}");

        public void NotifyTouchUp(uint timeMs, int id) => Log.Add($"touch-up {id}");

        public void NotifyTouchCancel() => Log.Add("touch-cancel");

        public void NotifyTextCommit(ReadOnlySpan<char> text) => Log.Add($"text {new string(text)}");

        public void NotifyPreedit(ReadOnlySpan<char> text, int cursorBegin, int cursorEnd) =>
            Log.Add($"preedit {new string(text)}");

        public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _observers.Destroyed(this);
            if (!_buffer.IsDestroyed)
            {
                _buffer.Destroy();
            }
        }
    }
}
