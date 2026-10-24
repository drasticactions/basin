using System.Diagnostics;
using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Seat;

/// <summary>
/// Applies the keyboard aids of <see cref="KeyboardAidOptions"/> to physical keys, before bindings and clients see them.
/// Bounce keys run first, then slow keys, then mouse keys, then sticky keys. Every key that passes goes to the delivery callback in order.
/// Times are the event times of the keys, so <see cref="Advance"/> with a stamped time gives the same result as the loop timer.
/// </summary>
public sealed class KeyboardAids : IDisposable
{
    private const uint LeftShift = 42;
    private const uint RightShift = 54;
    private const uint LeftControl = 29;
    private const uint RightControl = 97;
    private const uint LeftAlt = 56;
    private const uint RightAlt = 100;
    private const uint LeftSuper = 125;
    private const uint RightSuper = 126;
    private const uint ButtonLeft = 0x110;
    private const uint ButtonRight = 0x111;
    private const uint ButtonMiddle = 0x112;
    private const int TickMs = 16;
    private const double MinimumSpeed = 40;
    private const double FirstStep = 4;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Action<uint, uint, bool> _deliver;
    private readonly IEventSource? _timer;
    private readonly Dictionary<uint, uint> _lastRelease = [];
    private readonly HashSet<uint> _bounced = [];
    private readonly Dictionary<uint, uint> _slowPending = [];
    private readonly HashSet<uint> _slowAccepted = [];
    private readonly Dictionary<uint, uint> _mouseHeld = [];
    private readonly Dictionary<uint, bool> _heldModifiers = [];
    private readonly Dictionary<uint, bool> _sticky = [];
    private readonly HashSet<uint> _swallowRelease = [];
    private readonly HashSet<uint> _unlocking = [];
    private readonly List<uint> _scratch = [];
    private readonly List<uint> _accepting = [];
    private KeyboardAidOptions _options = new();
    private uint _button = ButtonLeft;
    private uint _mouseLast;
    private double _carryX;
    private double _carryY;
    private int _shiftPresses;
    private bool _timerArmed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardAids"/> class. Every aid starts off.
    /// </summary>
    /// <param name="deliver">Receives each key that passes, with its time, evdev code and press state.</param>
    /// <param name="loop">The loop for the timer that accepts slow keys and moves the pointer, or <see langword="null"/> when a caller drives <see cref="Advance"/>.</param>
    public KeyboardAids(Action<uint, uint, bool> deliver, ICompositorEventLoop? loop = null)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        _deliver = deliver;
        _timer = loop?.AddTimer(OnTimer);
    }

    /// <summary>
    /// Gets or sets the aids. Turning sticky keys off releases every latched or locked modifier. Turning slow keys or mouse keys off drops their held keys.
    /// </summary>
    public KeyboardAidOptions Options
    {
        get => _options;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _thread.Assert();
            var previous = _options;
            _options = value;
            if (previous.Sticky && !value.Sticky)
            {
                ReleaseSticky(Now());
            }

            if (value.SlowMs <= 0)
            {
                _slowPending.Clear();
            }

            if (!value.MouseKeys)
            {
                _mouseHeld.Clear();
            }
        }
    }

    /// <summary>
    /// Gets or sets the pointer that mouse keys move and click, or <see langword="null"/> to leave the keypad alone.
    /// </summary>
    public ISyntheticInput? Pointer { get; set; }

    /// <summary>
    /// Gets or sets the announcer that speaks each change, such as a latched modifier, or <see langword="null"/> for silence.
    /// </summary>
    public IAnnouncer? Announcer { get; set; }

    /// <summary>
    /// Occurs after five taps of Shift turned sticky keys on or off, with the new options.
    /// </summary>
    public event Action<KeyboardAidOptions>? OptionsChanged;

    /// <summary>
    /// Gets a value indicating whether any aid is on.
    /// </summary>
    public bool IsActive => _options.Sticky || _options.StickyShortcut || _options.SlowMs > 0 || _options.BounceMs > 0 || (_options.MouseKeys && Pointer is not null);

    /// <summary>
    /// Takes one physical key.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds of the monotonic clock.</param>
    /// <param name="key">The evdev key code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void Process(uint timeMs, uint key, bool pressed)
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        Advance(timeMs);
        Bounce(timeMs, key, pressed);
        Arm();
    }

    /// <summary>
    /// Accepts the slow keys that stayed down long enough and moves the pointer for held mouse keys, up to a time.
    /// </summary>
    /// <param name="nowMs">The time in milliseconds of the monotonic clock.</param>
    public void Advance(uint nowMs)
    {
        _thread.Assert();
        if (_slowPending.Count > 0)
        {
            _accepting.Clear();
            foreach (var (key, pressedAt) in _slowPending)
            {
                if (unchecked(nowMs - pressedAt) >= (uint)_options.SlowMs)
                {
                    _accepting.Add(key);
                }
            }

            foreach (var key in _accepting)
            {
                var pressedAt = _slowPending[key];
                _slowPending.Remove(key);
                _slowAccepted.Add(key);
                Mouse(unchecked(pressedAt + (uint)_options.SlowMs), key, true);
            }
        }

        MoveHeld(nowMs);
    }

    /// <summary>
    /// Stops the timer. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Remove();
    }

    private static uint Now() => (uint)(Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000));

    private static bool IsModifier(uint key) =>
        key is LeftShift or RightShift or LeftControl or RightControl or LeftAlt or RightAlt or LeftSuper or RightSuper;

    private static string NameOf(uint key) => key switch
    {
        LeftShift or RightShift => "Shift",
        LeftControl or RightControl => "Control",
        LeftAlt or RightAlt => "Alt",
        _ => "Super",
    };

    private static (int X, int Y)? Direction(uint key) => key switch
    {
        71 => (-1, -1),
        72 => (0, -1),
        73 => (1, -1),
        75 => (-1, 0),
        77 => (1, 0),
        79 => (-1, 1),
        80 => (0, 1),
        81 => (1, 1),
        _ => null,
    };

    private static bool IsMouseKey(uint key) => Direction(key) is not null || key is 76 or 82 or 83 or 98 or 55 or 74;

    private void Bounce(uint timeMs, uint key, bool pressed)
    {
        if (_options.BounceMs <= 0)
        {
            Slow(timeMs, key, pressed);
            return;
        }

        if (pressed)
        {
            if (_lastRelease.TryGetValue(key, out var released) && unchecked(timeMs - released) < (uint)_options.BounceMs)
            {
                _bounced.Add(key);
                return;
            }

            Slow(timeMs, key, true);
            return;
        }

        if (_bounced.Remove(key))
        {
            return;
        }

        _lastRelease[key] = timeMs;
        Slow(timeMs, key, false);
    }

    private void Slow(uint timeMs, uint key, bool pressed)
    {
        if (_options.SlowMs <= 0)
        {
            if (!pressed && _slowAccepted.Remove(key))
            {
                Mouse(timeMs, key, false);
                return;
            }

            Mouse(timeMs, key, pressed);
            return;
        }

        if (pressed)
        {
            if (!_slowPending.ContainsKey(key) && !_slowAccepted.Contains(key))
            {
                _slowPending[key] = timeMs;
            }

            return;
        }

        if (_slowPending.Remove(key))
        {
            return;
        }

        if (_slowAccepted.Remove(key))
        {
            Mouse(timeMs, key, false);
        }
    }

    private void Mouse(uint timeMs, uint key, bool pressed)
    {
        if (!_options.MouseKeys || Pointer is not { } pointer || !IsMouseKey(key))
        {
            Sticky(timeMs, key, pressed);
            return;
        }

        if (Direction(key) is { } direction)
        {
            if (!pressed)
            {
                _mouseHeld.Remove(key);
                return;
            }

            if (_mouseHeld.Count == 0)
            {
                _mouseLast = timeMs;
                (_carryX, _carryY) = (0, 0);
            }

            _mouseHeld[key] = timeMs;
            MoveBy(pointer, timeMs, direction.X * FirstStep, direction.Y * FirstStep);
            return;
        }

        if (!pressed)
        {
            return;
        }

        switch (key)
        {
            case 76:
                pointer.PointerButton(timeMs, _button, true);
                pointer.PointerButton(timeMs, _button, false);
                break;
            case 82:
                pointer.PointerButton(timeMs, _button, true);
                break;
            case 83:
                pointer.PointerButton(timeMs, _button, false);
                break;
            case 98:
                _button = ButtonLeft;
                break;
            case 55:
                _button = ButtonMiddle;
                break;
            case 74:
                _button = ButtonRight;
                break;
        }
    }

    private void MoveHeld(uint nowMs)
    {
        if (_mouseHeld.Count == 0 || Pointer is not { } pointer)
        {
            return;
        }

        var elapsed = unchecked(nowMs - _mouseLast);
        if (elapsed == 0 || elapsed > int.MaxValue)
        {
            return;
        }

        var x = 0;
        var y = 0;
        var first = nowMs;
        foreach (var (key, pressedAt) in _mouseHeld)
        {
            var direction = Direction(key)!.Value;
            x += direction.X;
            y += direction.Y;
            if (unchecked(nowMs - pressedAt) > unchecked(nowMs - first))
            {
                first = pressedAt;
            }
        }

        _mouseLast = nowMs;
        if (x == 0 && y == 0)
        {
            return;
        }

        var ramp = _options.MouseKeysAccelerationMs <= 0 ? 1.0 : Math.Min(1.0, unchecked(nowMs - first) / (double)_options.MouseKeysAccelerationMs);
        var speed = Math.Max(MinimumSpeed, _options.MouseKeysMaxSpeed * ramp);
        var length = Math.Sqrt((x * x) + (y * y));
        var distance = speed * elapsed / 1000.0;
        _carryX += x / length * distance;
        _carryY += y / length * distance;
        var stepX = Math.Truncate(_carryX);
        var stepY = Math.Truncate(_carryY);
        _carryX -= stepX;
        _carryY -= stepY;
        if (stepX != 0 || stepY != 0)
        {
            MoveBy(pointer, nowMs, stepX, stepY);
        }
    }

    private static void MoveBy(ISyntheticInput pointer, uint timeMs, double dx, double dy)
    {
        if (pointer.TryPointerPosition(out var x, out var y))
        {
            pointer.PointerMotionAbsolute(timeMs, x + dx, y + dy);
        }
    }

    private void Sticky(uint timeMs, uint key, bool pressed)
    {
        if (!IsModifier(key))
        {
            if (pressed)
            {
                _shiftPresses = 0;
                _scratch.Clear();
                foreach (var held in _heldModifiers.Keys)
                {
                    _scratch.Add(held);
                }

                foreach (var held in _scratch)
                {
                    _heldModifiers[held] = true;
                }

                _deliver(timeMs, key, true);
                return;
            }

            _deliver(timeMs, key, false);
            ReleaseLatched(timeMs);
            return;
        }

        if (pressed)
        {
            var toggled = CountShift(timeMs, key);
            if (_options.Sticky && !toggled && _sticky.TryGetValue(key, out var locked))
            {
                if (!locked)
                {
                    _sticky[key] = true;
                    _swallowRelease.Add(key);
                    Announce($"{NameOf(key)} locked");
                }
                else
                {
                    _sticky.Remove(key);
                    _unlocking.Add(key);
                    Announce($"{NameOf(key)} released");
                }

                return;
            }

            _heldModifiers[key] = toggled;
            _deliver(timeMs, key, true);
            return;
        }

        if (_swallowRelease.Remove(key))
        {
            return;
        }

        if (_unlocking.Remove(key))
        {
            _deliver(timeMs, key, false);
            return;
        }

        if (_heldModifiers.Remove(key, out var used) && !used && _options.Sticky)
        {
            _sticky[key] = false;
            Announce($"{NameOf(key)} latched");
            return;
        }

        _deliver(timeMs, key, false);
    }

    private bool CountShift(uint timeMs, uint key)
    {
        if (key is not (LeftShift or RightShift))
        {
            _shiftPresses = 0;
            return false;
        }

        if (!_options.StickyShortcut || ++_shiftPresses < 5)
        {
            return false;
        }

        _shiftPresses = 0;
        var sticky = !_options.Sticky;
        if (!sticky)
        {
            ReleaseSticky(timeMs);
            _heldModifiers.Remove(key);
        }

        _options = _options with { Sticky = sticky };
        Announce(sticky ? "Sticky keys on" : "Sticky keys off");
        OptionsChanged?.Invoke(_options);
        return true;
    }

    private void ReleaseLatched(uint timeMs)
    {
        if (_sticky.Count == 0)
        {
            return;
        }

        _scratch.Clear();
        foreach (var (key, locked) in _sticky)
        {
            if (!locked)
            {
                _scratch.Add(key);
            }
        }

        foreach (var key in _scratch)
        {
            _sticky.Remove(key);
            _deliver(timeMs, key, false);
        }
    }

    private void ReleaseSticky(uint timeMs)
    {
        _scratch.Clear();
        foreach (var key in _sticky.Keys)
        {
            _scratch.Add(key);
        }

        _sticky.Clear();
        foreach (var key in _scratch)
        {
            if (!_swallowRelease.Contains(key))
            {
                _deliver(timeMs, key, false);
            }
            else
            {
                _swallowRelease.Remove(key);
                _unlocking.Add(key);
            }
        }
    }

    private void Announce(string text)
    {
        try
        {
            Announcer?.Announce(text);
        }
        catch (Exception error)
        {
            SeatLog.Log.Warn(error, $"keyboard aids: the announcer failed");
        }
    }

    private void Arm()
    {
        if (_timer is null)
        {
            return;
        }

        var needed = _slowPending.Count > 0 || _mouseHeld.Count > 0;
        if (needed != _timerArmed)
        {
            _timerArmed = needed;
            _timer.UpdateTimer(needed ? TickMs : 0);
        }
    }

    private void OnTimer()
    {
        _timerArmed = false;
        if (_disposed)
        {
            return;
        }

        try
        {
            Advance(Now());
        }
        catch (Exception error)
        {
            SeatLog.Log.Warn(error, $"keyboard aids: the timer failed");
        }

        Arm();
    }
}
