using Basin.Capabilities;
using Basin.Scene;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class UISurfaceRouterTests
{
    [Fact]
    public void A_hover_change_pairs_a_leave_with_an_enter()
    {
        using var fixture = new RouterFixture();
        var left = fixture.Add(new Box(0, 0, 100, 100));
        var right = fixture.Add(new Box(200, 0, 100, 100));

        var route = fixture.Router.PointerMotion(1, 10, 20);
        Assert.Same(left.Surface, route.Surface);
        Assert.True(route.Entered);
        Assert.Equal("enter 10,20", left.Surface.Log[0]);

        route = fixture.Router.PointerMotion(2, 30, 40);
        Assert.False(route.Entered);
        Assert.Equal("motion 30,40", left.Surface.Log[1]);

        route = fixture.Router.PointerMotion(3, 210, 40);
        Assert.Same(right.Surface, route.Surface);
        Assert.True(route.Entered);
        Assert.Equal("leave", left.Surface.Log[2]);
        Assert.Equal("enter 10,40", right.Surface.Log[0]);

        Assert.Null(fixture.Router.PointerMotion(4, 500, 500).Surface);
        Assert.Equal("leave", right.Surface.Log[1]);
        Assert.Null(fixture.Router.Hovered);
    }

    [Fact]
    public void Layout_coordinates_become_surface_local_ones()
    {
        using var fixture = new RouterFixture();
        var branch = new SceneTree(fixture.Scene.Root);
        branch.SetPosition(50, 60);
        var panel = fixture.Add(new Box(4, 6, 80, 40), branch);

        var route = fixture.Router.PointerMotion(1, 60, 70);
        Assert.Same(panel.Surface, route.Surface);
        Assert.Equal("enter 6,4", panel.Surface.Log[0]);

        Assert.True(fixture.Router.TryLocal(panel.Surface, 60, 70, out var localX, out var localY));
        Assert.Equal(6, localX);
        Assert.Equal(4, localY);

        branch.Destroy();
    }

    [Fact]
    public void Buttons_axis_and_touch_reach_the_hovered_surface()
    {
        using var fixture = new RouterFixture();
        var panel = fixture.Add(new Box(0, 0, 100, 100));

        Assert.False(fixture.Router.PointerButton(1, 272, pressed: true));
        Assert.False(fixture.Router.TouchDown(1, 0, 5, 5));

        fixture.Router.PointerMotion(1, 10, 10);
        Assert.True(fixture.Router.PointerButton(2, 272, pressed: true));
        Assert.True(fixture.Router.PointerAxis(3, 0, 15));
        Assert.True(fixture.Router.TouchDown(4, 7, 20, 30));
        Assert.True(fixture.Router.TouchUp(5, 7));

        Assert.Contains("button 272 down", panel.Surface.Log);
        Assert.Contains("axis 0,15", panel.Surface.Log);
        Assert.Contains("touch-down 7 20,30", panel.Surface.Log);
        Assert.Contains("touch-up 7", panel.Surface.Log);
    }

    [Fact]
    public void A_named_target_beats_the_hovered_surface()
    {
        using var fixture = new RouterFixture();
        var hovered = fixture.Add(new Box(0, 0, 100, 100));
        var latched = fixture.Add(new Box(200, 0, 100, 100));

        fixture.Router.PointerMotion(1, 10, 10);
        Assert.True(fixture.Router.PointerButton(2, 272, pressed: true, latched.Surface));

        Assert.Contains("button 272 down", latched.Surface.Log);
        Assert.DoesNotContain("button 272 down", hovered.Surface.Log);
    }

    [Fact]
    public void Keyboard_focus_pairs_an_enter_with_a_leave_and_reports_text_input()
    {
        using var fixture = new RouterFixture();
        var dialog = fixture.Add(new Box(0, 0, 100, 100));
        var other = fixture.Add(new Box(200, 0, 100, 100));

        Assert.False(fixture.Router.Key(1, 30, pressed: true));
        Assert.False(fixture.Router.WantsTextInput);

        dialog.Surface.TextInput = true;
        fixture.Router.SetKeyboardFocus(dialog.Surface, [42u]);
        Assert.Same(dialog.Surface, fixture.Router.KeyboardFocus);
        Assert.True(fixture.Router.WantsTextInput);
        Assert.Equal("kbd-enter 42", dialog.Surface.Log[0]);

        Assert.True(fixture.Router.Key(2, 30, pressed: true));
        Assert.True(fixture.Router.Modifiers(1, 0, 0, 0));
        Assert.True(fixture.Router.TextCommit("hi"));

        fixture.Router.SetKeyboardFocus(other.Surface);
        Assert.Contains("kbd-leave", dialog.Surface.Log);
        Assert.Contains("kbd-enter", other.Surface.Log);
        Assert.False(fixture.Router.WantsTextInput);

        fixture.Router.SetKeyboardFocus(null);
        Assert.Null(fixture.Router.KeyboardFocus);
        Assert.False(fixture.Router.Key(3, 30, pressed: false));
    }

    [Fact]
    public void A_destroyed_surface_stops_being_hovered_and_focused()
    {
        using var fixture = new RouterFixture();
        var panel = fixture.Add(new Box(0, 0, 100, 100));

        fixture.Router.PointerMotion(1, 10, 10);
        fixture.Router.SetKeyboardFocus(panel.Surface);
        Assert.NotNull(fixture.Router.Hovered);

        panel.Node.Dispose();
        panel.Surface.Dispose();

        Assert.Null(fixture.Router.Hovered);
        Assert.Null(fixture.Router.KeyboardFocus);
        Assert.False(fixture.Router.PointerButton(2, 272, pressed: true));
    }

    private sealed class RouterFixture : IDisposable
    {
        private readonly List<Placed> _placed = [];

        public Scene.Scene Scene { get; } = new();

        public UISurfaceIndex Index { get; } = new();

        public UISurfaceRouter Router { get; }

        public RouterFixture() => Router = new UISurfaceRouter(Scene, Index);

        public Placed Add(in Box box, SceneTree? parent = null)
        {
            var surface = new RecordingUISurface(box.Width, box.Height);
            var node = new UISurfaceNode(parent ?? Scene.Root, surface, Index);
            node.SetPosition(box.X, box.Y);
            node.Publish();
            var entry = new Placed(surface, node);
            _placed.Add(entry);
            return entry;
        }

        public void Dispose()
        {
            foreach (var entry in _placed)
            {
                entry.Node.Dispose();
                entry.Surface.Dispose();
            }

            _placed.Clear();
            Scene.Root.Destroy();
        }
    }

    private sealed record Placed(RecordingUISurface Surface, UISurfaceNode Node);

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
