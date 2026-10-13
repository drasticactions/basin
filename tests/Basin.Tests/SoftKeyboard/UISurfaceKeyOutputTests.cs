using Basin.Capabilities;
using Basin.Scene;
using Basin.SoftKeyboard;
using Xunit;

namespace Basin.Tests;

public sealed class UISurfaceKeyOutputTests
{
    [Fact]
    public void Keys_text_and_a_control_chord_reach_the_focused_surface()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymap(new KeymapNames(Layout: "us"));
        var scene = new Scene.Scene();
        var index = new UISurfaceIndex();
        using var surface = new KeyRecordingSurface();
        using (var node = new UISurfaceNode(scene.Root, surface, index))
        {
            node.Publish();
            var router = new UISurfaceRouter(scene, index);
            var output = new UISurfaceKeyOutput(router, keyboard);

            output.Key(30, 1);
            Assert.Empty(surface.Log);

            router.SetKeyboardFocus(surface);
            surface.Log.Clear();
            output.Key(30, 2);
            Assert.Equal(["key 30 down", "text a", "key 30 up"], surface.Log);

            surface.Log.Clear();
            output.Key(14, 3);
            Assert.Equal(["key 14 down", "key 14 up"], surface.Log);

            surface.Log.Clear();
            output.ControlKey(14, 4);
            var control = 1u << (int)keyboard.Keymap!.GetModIndex("Control")!.Value;
            Assert.Equal([$"mods {control}", "key 14 down", "key 14 up", "mods 0"], surface.Log);

            surface.Log.Clear();
            Assert.Equal(4, output.Type("pä$s", 5));
            Assert.Equal(["text pä$s"], surface.Log);
            Assert.Equal(0, output.Type("", 6));

            Assert.True(output.TryResolve('A', out var keycode, out var modifiers));
            Assert.Equal(30u, keycode);
            Assert.NotEqual(0u, modifiers);
        }

        scene.Root.Destroy();
    }

    private sealed class KeyRecordingSurface : IUISurface
    {
        private readonly UISurfaceObservers _observers = new();
        private readonly MemoryBuffer _buffer = new(16, 16, DrmFormat.Argb8888);

        public List<string> Log { get; } = [];

        public UISurfaceSize Size => new(16, 16, 1.0);

        public bool WantsTextInput => true;

        public bool Configure(int logicalWidth, int logicalHeight, double scale) => true;

        public bool TryAcquire(out UIFrame frame)
        {
            frame = new UIFrame(_buffer.Lock(), damage: null);
            return true;
        }

        public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

        public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

        public bool AcceptsInputAt(double x, double y) => true;

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

        public void NotifyKeyboardEnter(ReadOnlySpan<uint> pressed)
        {
        }

        public void NotifyKey(uint timeMs, uint key, bool pressed) => Log.Add($"key {key} {(pressed ? "down" : "up")}");

        public void NotifyModifiers(uint depressed, uint latched, uint locked, uint group) => Log.Add($"mods {depressed}");

        public void NotifyKeyboardLeave()
        {
        }

        public void NotifyTouchDown(uint timeMs, int id, double x, double y)
        {
        }

        public void NotifyTouchMotion(uint timeMs, int id, double x, double y)
        {
        }

        public void NotifyTouchUp(uint timeMs, int id)
        {
        }

        public void NotifyTouchCancel()
        {
        }

        public void NotifyTextCommit(ReadOnlySpan<char> text) => Log.Add($"text {new string(text)}");

        public void NotifyPreedit(ReadOnlySpan<char> text, int cursorBegin, int cursorEnd)
        {
        }

        public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

        public void Dispose()
        {
            _observers.Destroyed(this);
            if (!_buffer.IsDestroyed)
            {
                _buffer.Destroy();
            }
        }
    }
}
