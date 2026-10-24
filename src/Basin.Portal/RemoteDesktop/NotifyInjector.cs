using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Portal;

/// <summary>
/// Turns the RemoteDesktop <c>Notify*</c> calls into <see cref="IInputSink"/> events. It records pressed buttons, keys and touches, so that <see cref="ReleaseAll"/> can release them.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="NotifyInjector"/> class.
    /// </summary>
    /// <param name="sink">The sink that receives the events.</param>
    /// <param name="keycodes">The keymap lookup for keysym input, or <see langword="null"/> to ignore keysyms.</param>
    /// <param name="layout">The layout whose bounds scale the absolute positions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sink"/> or <paramref name="layout"/> is <see langword="null"/>.</exception>
    public NotifyInjector(IInputSink sink, IKeymapLookup? keycodes, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(layout);
        _sink = sink;
        _keycodes = keycodes;
        _layout = layout;
    }

    /// <summary>
    /// Gets or sets the function that gives the layout box of a stream from its PipeWire node id. Absolute and touch events for a stream with no box are dropped.
    /// </summary>
    public Func<uint, Box?>? StreamBox { get; set; }

    /// <summary>
    /// Gets the number of pointer buttons that are pressed now.
    /// </summary>
    public int PressedButtons => _pressedButtons.Count;

    /// <summary>
    /// Gets the number of keys that are pressed now.
    /// </summary>
    public int PressedKeys => _pressedKeys.Count;

    private static uint Now => (uint)(MonotonicClock.Nanos / 1_000_000);

    /// <summary>
    /// Moves the pointer by a relative amount and sends a frame.
    /// </summary>
    /// <param name="dx">The horizontal motion in logical pixels.</param>
    /// <param name="dy">The vertical motion in logical pixels.</param>
    public void Motion(double dx, double dy)
    {
        _sink.PointerMotion(Now, dx, dy);
        _sink.Frame();
    }

    /// <summary>
    /// Moves the pointer to a position in a stream and sends a frame. The sink receives it relative to the layout bounds, with the bounds size as the extent. The position is clamped to the stream box.
    /// </summary>
    /// <param name="stream">The PipeWire node id of the stream.</param>
    /// <param name="x">The x position in the stream, in logical pixels.</param>
    /// <param name="y">The y position in the stream, in logical pixels.</param>
    public void MotionAbsolute(uint stream, double x, double y)
    {
        if (!TryMap(stream, x, y, out var layoutX, out var layoutY))
        {
            return;
        }

        var bounds = _layout.Bounds;
        _sink.PointerMotionAbsolute(Now, layoutX - bounds.X, layoutY - bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        _sink.Frame();
    }

    /// <summary>
    /// Presses or releases a pointer button and sends a frame.
    /// </summary>
    /// <param name="button">The evdev button code.</param>
    /// <param name="pressed"><see langword="true"/> to press.</param>
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

    /// <summary>
    /// Sends a finger scroll on each axis that moved, and a frame.
    /// </summary>
    /// <param name="dx">The horizontal scroll distance.</param>
    /// <param name="dy">The vertical scroll distance.</param>
    /// <param name="finish"><see langword="true"/> to stop the scroll on both axes.</param>
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

    /// <summary>
    /// Sends wheel steps as a wheel scroll of 15 units per step, and a frame.
    /// </summary>
    /// <param name="axis">1 for horizontal. Any other value is vertical.</param>
    /// <param name="steps">The number of steps, with the sign for the direction.</param>
    public void AxisDiscrete(uint axis, int steps)
    {
        _sink.PointerAxisSource(SourceWheel);
        _sink.PointerAxis(Now, axis == AxisHorizontal ? AxisHorizontal : AxisVertical, steps * 15.0);
        _sink.Frame();
    }

    /// <summary>
    /// Presses or releases a key on the injected keyboard, which the first key creates.
    /// </summary>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> to press.</param>
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

    /// <summary>
    /// Presses or releases the key that types a keysym. When the keysym needs Shift and Shift is not down, the call wraps the key in a Shift press and release.
    /// </summary>
    /// <param name="keysym">The XKB keysym.</param>
    /// <param name="pressed"><see langword="true"/> to press.</param>
    /// <returns><see langword="false"/> when no keymap lookup is set or no key types the keysym.</returns>
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

    /// <summary>
    /// Starts a touch point at a position in a stream. The sink receives it relative to the layout bounds, with the bounds size as the extent. The position is clamped to the stream box.
    /// </summary>
    /// <param name="stream">The PipeWire node id of the stream.</param>
    /// <param name="slot">The touch slot.</param>
    /// <param name="x">The x position in the stream, in logical pixels.</param>
    /// <param name="y">The y position in the stream, in logical pixels.</param>
    public void TouchDown(uint stream, uint slot, double x, double y)
    {
        if (!TryMap(stream, x, y, out var layoutX, out var layoutY))
        {
            return;
        }

        var bounds = _layout.Bounds;
        _touches.Add((int)slot);
        if (_sink.TouchDown(Now, (int)slot, layoutX - bounds.X, layoutY - bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)))
        {
            _sink.TouchFrame();
        }
    }

    /// <summary>
    /// Moves a touch point to a position in a stream. The sink receives it relative to the layout bounds, with the bounds size as the extent. The position is clamped to the stream box.
    /// </summary>
    /// <param name="stream">The PipeWire node id of the stream.</param>
    /// <param name="slot">The touch slot.</param>
    /// <param name="x">The x position in the stream, in logical pixels.</param>
    /// <param name="y">The y position in the stream, in logical pixels.</param>
    public void TouchMotion(uint stream, uint slot, double x, double y)
    {
        if (!TryMap(stream, x, y, out var layoutX, out var layoutY))
        {
            return;
        }

        var bounds = _layout.Bounds;
        if (_sink.TouchMotion(Now, (int)slot, layoutX - bounds.X, layoutY - bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)))
        {
            _sink.TouchFrame();
        }
    }

    /// <summary>
    /// Ends a touch point.
    /// </summary>
    /// <param name="slot">The touch slot.</param>
    public void TouchUp(uint slot)
    {
        _touches.Remove((int)slot);
        if (_sink.TouchUp(Now, (int)slot))
        {
            _sink.TouchFrame();
        }
    }

    /// <summary>
    /// Releases every pressed button and key, and cancels the touch points.
    /// </summary>
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

    /// <summary>
    /// Releases everything that is pressed and destroys the injected keyboard.
    /// </summary>
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
