using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Portal;

public sealed class NotifyInjector : IDisposable
{
    private const uint AxisVertical = 0;
    private const uint AxisHorizontal = 1;
    private const uint SourceWheel = 0;
    private const uint SourceFinger = 1;
    private const uint KeyLeftShift = 42;

    private readonly IInputSink _sink;
    private readonly IKeymapLookup? _keycodes;
    private readonly OutputLayout _layout;
    private readonly HashSet<uint> _pressedButtons = [];
    private readonly HashSet<uint> _pressedKeys = [];
    private readonly HashSet<int> _touches = [];
    private IInjectedKeyboard? _keyboard;
    private bool _keyboardTried;

    public NotifyInjector(IInputSink sink, IKeymapLookup? keycodes, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(layout);
        _sink = sink;
        _keycodes = keycodes;
        _layout = layout;
    }

    public Func<uint, Box?>? StreamBox { get; set; }

    public int PressedButtons => _pressedButtons.Count;

    public int PressedKeys => _pressedKeys.Count;

    private static uint Now => (uint)(MonotonicClock.Nanos / 1_000_000);

    public void Motion(double dx, double dy)
    {
        _sink.PointerMotion(Now, dx, dy);
        _sink.Frame();
    }

    public void MotionAbsolute(uint stream, double x, double y)
    {
        if (!TryMap(stream, x, y, out var layoutX, out var layoutY))
        {
            return;
        }

        var bounds = _layout.Bounds;
        _sink.PointerMotionAbsolute(Now, layoutX, layoutY, Math.Max(1, bounds.Right), Math.Max(1, bounds.Bottom));
        _sink.Frame();
    }

    public void Button(int button, bool pressed)
    {
        var code = unchecked((uint)button);
        if (pressed)
        {
            _pressedButtons.Add(code);
        }
        else
        {
            _pressedButtons.Remove(code);
        }

        _sink.PointerButton(Now, code, pressed);
        _sink.Frame();
    }

    public void Axis(double dx, double dy, bool finish)
    {
        var time = Now;
        _sink.PointerAxisSource(SourceFinger);
        if (dy != 0)
        {
            _sink.PointerAxis(time, AxisVertical, dy);
        }

        if (dx != 0)
        {
            _sink.PointerAxis(time, AxisHorizontal, dx);
        }

        if (finish)
        {
            _sink.PointerAxisStop(time, AxisVertical);
            _sink.PointerAxisStop(time, AxisHorizontal);
        }

        _sink.Frame();
    }

    public void AxisDiscrete(uint axis, int steps)
    {
        _sink.PointerAxisSource(SourceWheel);
        _sink.PointerAxis(Now, axis == AxisHorizontal ? AxisHorizontal : AxisVertical, steps * 15.0);
        _sink.Frame();
    }

    public void Keycode(int keycode, bool pressed)
    {
        var code = unchecked((uint)keycode);
        EnsureKeyboard();
        if (pressed)
        {
            _pressedKeys.Add(code);
        }
        else
        {
            _pressedKeys.Remove(code);
        }

        _sink.Key(_keyboard, Now, code, pressed);
    }

    public bool Keysym(int keysym, bool pressed)
    {
        if (_keycodes is null || !_keycodes.TryKeycodeForKeysym(unchecked((uint)keysym), out var keycode, out var modifiers))
        {
            return false;
        }

        EnsureKeyboard();
        var time = Now;
        var shifted = (modifiers & 1) != 0 && !_pressedKeys.Contains(KeyLeftShift);
        if (pressed && shifted)
        {
            _sink.Key(_keyboard, time, KeyLeftShift, true);
        }

        if (pressed)
        {
            _pressedKeys.Add(keycode);
        }
        else
        {
            _pressedKeys.Remove(keycode);
        }

        _sink.Key(_keyboard, time, keycode, pressed);
        if (!pressed && shifted)
        {
            _sink.Key(_keyboard, time, KeyLeftShift, false);
        }

        return true;
    }

    public void TouchDown(uint stream, uint slot, double x, double y)
    {
        if (!TryMap(stream, x, y, out var layoutX, out var layoutY))
        {
            return;
        }

        var bounds = _layout.Bounds;
        _touches.Add((int)slot);
        if (_sink.TouchDown(Now, (int)slot, layoutX, layoutY, Math.Max(1, bounds.Right), Math.Max(1, bounds.Bottom)))
        {
            _sink.TouchFrame();
        }
    }

    public void TouchMotion(uint stream, uint slot, double x, double y)
    {
        if (!TryMap(stream, x, y, out var layoutX, out var layoutY))
        {
            return;
        }

        var bounds = _layout.Bounds;
        if (_sink.TouchMotion(Now, (int)slot, layoutX, layoutY, Math.Max(1, bounds.Right), Math.Max(1, bounds.Bottom)))
        {
            _sink.TouchFrame();
        }
    }

    public void TouchUp(uint slot)
    {
        _touches.Remove((int)slot);
        if (_sink.TouchUp(Now, (int)slot))
        {
            _sink.TouchFrame();
        }
    }

    public void ReleaseAll()
    {
        var time = Now;
        foreach (var button in _pressedButtons)
        {
            _sink.PointerButton(time, button, false);
        }

        if (_pressedButtons.Count > 0)
        {
            _sink.Frame();
        }

        _pressedButtons.Clear();
        foreach (var key in _pressedKeys)
        {
            _sink.Key(_keyboard, time, key, false);
        }

        _pressedKeys.Clear();
        if (_touches.Count > 0)
        {
            _sink.TouchCancel();
            _touches.Clear();
        }
    }

    public void Dispose()
    {
        ReleaseAll();
        _keyboard?.Dispose();
        _keyboard = null;
    }

    private void EnsureKeyboard()
    {
        if (_keyboardTried)
        {
            return;
        }

        _keyboardTried = true;
        _keyboard = _sink.CreateKeyboard();
    }

    private bool TryMap(uint stream, double x, double y, out double layoutX, out double layoutY)
    {
        var box = StreamBox?.Invoke(stream);
        if (box is not { } b || b.IsEmpty)
        {
            layoutX = layoutY = 0;
            return false;
        }

        layoutX = b.X + Math.Clamp(x, 0, b.Width);
        layoutY = b.Y + Math.Clamp(y, 0, b.Height);
        return true;
    }
}
