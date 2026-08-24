using Basin.Backend.Drm;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Capabilities.Defaults;
using Basin.Desktop;

namespace Basin.Seat.Backends;

public sealed class SeatBinder
{
    private readonly Seat _seat;
    private readonly OutputLayout _layout;
    private readonly LayoutPointer _pointer;
    private readonly CursorController _cursor;

    private int _keyboards;
    private readonly List<InputDevice> _keyboardDevices = [];
    private int _pointers;
    private int _touchDevices;
    private bool _cursorLoaded;

    public SeatBinder(Seat seat, OutputLayout layout, LayoutPointer pointer, CursorController cursor)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentNullException.ThrowIfNull(cursor);

        _seat = seat;
        _layout = layout;
        _pointer = pointer;
        _cursor = cursor;
    }

    public DrmBackend? Drm { get; set; }

    public CursorImageTheme? Theme { get; set; }

    public bool AdoptParentKeymap { get; set; }

    public Func<InputDevice, Basin.Capabilities.IInjectedKeyboard?>? KeyboardFor { get; set; }

    public Func<bool>? PointerFrozen { get; set; }

    public event Action<InputDevice>? DeviceAdded;

    public event Action<InputDevice>? DeviceRemoved;

    public event Action<uint, uint, bool>? Key;

    public event Action? ModifiersChanged;

    public event SeatMotionHandler? Motion;

    public event Action<uint, uint, bool>? Button;

    public event Action<uint, PointerAxis>? Axis;

    public event Action? PointerLeft;

    public event Action<uint, int, double, double>? TouchDown;

    public event Action<uint, int, double, double>? TouchMotion;

    public event Action<uint, int>? TouchUp;

    public event Action? TouchFrame;

    public event Action? TouchCancelled;

    public void BindLibinput(LibinputBackend input, bool start = true)
    {
        ArgumentNullException.ThrowIfNull(input);

        BindLibinputLeds(input);
        input.DeviceAdded += device =>
        {
            if (device.HasKeyboard)
            {
                _keyboards++;
            }

            if (device.HasPointer)
            {
                _pointers++;
            }

            if (device.HasTouch)
            {
                _touchDevices++;
            }

            UpdateCapabilities();
            DeviceAdded?.Invoke(device);
        };
        input.DeviceRemoved += device =>
        {
            if (device.HasKeyboard)
            {
                _keyboards--;
            }

            if (device.HasPointer)
            {
                _pointers--;
            }

            if (device.HasTouch)
            {
                _touchDevices--;
            }

            UpdateCapabilities();
            DeviceRemoved?.Invoke(device);
        };

        input.Key += (device, timeMs, key, pressed) =>
        {
            _seat.Keyboard.Activate(KeyboardFor?.Invoke(device));
            Key?.Invoke(timeMs, key, pressed);
        };
        input.PointerMotion += (_, timeMs, dx, dy, unacceleratedDx, unacceleratedDy) =>
        {
            if (PointerFrozen?.Invoke() != true)
            {
                _pointer.Motion(dx, dy);
            }

            Motion?.Invoke(timeMs, dx, dy, unacceleratedDx, unacceleratedDy);
        };
        input.PointerMotionAbsolute += (device, timeMs, normalizedX, normalizedY) =>
        {
            if (PointerFrozen?.Invoke() == true)
            {
                return;
            }

            var previousX = _pointer.X;
            var previousY = _pointer.Y;
            _pointer.MotionAbsolute(OutputFor(device), normalizedX, normalizedY);
            Motion?.Invoke(timeMs, _pointer.X - previousX, _pointer.Y - previousY, null, null);
        };
        input.PointerButton += (_, timeMs, button, pressed) => Button?.Invoke(timeMs, button, pressed);
        input.PointerScroll += (_, timeMs, axis) => Axis?.Invoke(timeMs, axis);

        WireLibinputTouch(input);

        if (start)
        {
            input.Start();
        }
    }

    public void BindLibinputLeds(LibinputBackend input)
    {
        ArgumentNullException.ThrowIfNull(input);

        input.DeviceAdded += device =>
        {
            if (device.HasKeyboard)
            {
                _keyboardDevices.Add(device);
                device.UpdateLeds(MapLeds(_seat.Keyboard.Leds));
            }
        };
        input.DeviceRemoved += device =>
        {
            if (device.HasKeyboard)
            {
                _keyboardDevices.Remove(device);
            }
        };
        _seat.Keyboard.LedsChanged += PushLeds;
    }

    public void BindLibinputTouch(LibinputBackend input)
    {
        ArgumentNullException.ThrowIfNull(input);

        input.DeviceAdded += device =>
        {
            if (device.HasTouch)
            {
                _touchDevices++;
                _seat.SetCapability(SeatCapability.Touch, _touchDevices > 0);
            }
        };
        input.DeviceRemoved += device =>
        {
            if (device.HasTouch)
            {
                _touchDevices--;
                _seat.SetCapability(SeatCapability.Touch, _touchDevices > 0);
            }
        };

        WireLibinputTouch(input);
    }

    public void BindParentTouch(WaylandBackend parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        parent.TouchAdded += BindParentTouch;
    }

    private void WireLibinputTouch(LibinputBackend input)
    {
        input.TouchDown += (device, timeMs, slot, normalizedX, normalizedY) =>
        {
            if (TouchOutput(device) is not { } on)
            {
                return;
            }

            var (x, y) = _layout.FromNormalized(on, normalizedX, normalizedY);
            TouchDown?.Invoke(timeMs, slot, x, y);
        };
        input.TouchMotion += (device, timeMs, slot, normalizedX, normalizedY) =>
        {
            if (TouchOutput(device) is not { } on)
            {
                return;
            }

            var (x, y) = _layout.FromNormalized(on, normalizedX, normalizedY);
            TouchMotion?.Invoke(timeMs, slot, x, y);
        };
        input.TouchUp += (_, timeMs, slot) => TouchUp?.Invoke(timeMs, slot);
        input.TouchFrame += _ => TouchFrame?.Invoke();
        input.TouchCancel += _ => TouchCancelled?.Invoke();
    }

    public void BindParent(WaylandBackend parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        parent.KeyboardAdded += BindParentKeyboard;
        parent.PointerAdded += BindParentPointer;
        parent.TouchAdded += BindParentTouch;
    }

    public void EnsurePointerCapability()
    {
        if ((_seat.Capabilities & SeatCapability.Pointer) == 0)
        {
            _seat.SetCapability(SeatCapability.Pointer, true);
            EnsureCursorLoaded();
        }
    }

    public void EnsureCursorLoaded()
    {
        if (_cursorLoaded || _cursor.Images is not null)
        {
            return;
        }

        _cursorLoaded = true;
        var size = CursorSizeFromEnvironment();
        if (Drm is { } drm)
        {
            var (width, height) = drm.CursorSize;
            _cursor.Load(new DumbAllocator(drm), width, height, size);
        }
        else
        {
            _cursor.Load(new ShmAllocator(), 128, 128, size);
        }

        if (Theme is { } theme)
        {
            theme.Images = _cursor.Images;
        }
    }

    public static int CursorSizeFromEnvironment()
    {
        var text = Environment.GetEnvironmentVariable("XCURSOR_SIZE");
        return int.TryParse(text, out var size) && size > 0 ? size : 24;
    }

    public IOutput? OutputFor(InputDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.OutputName is not { } name)
        {
            return null;
        }

        foreach (var (output, _) in _layout.Outputs)
        {
            if (output.Name == name)
            {
                return output;
            }
        }

        return null;
    }

    public IOutput? TouchOutput(InputDevice device) =>
        OutputFor(device) ?? (_layout.Outputs.Count > 0 ? _layout.Outputs[0].Output : null);

    private void PushLeds()
    {
        var leds = MapLeds(_seat.Keyboard.Leds);
        foreach (var device in _keyboardDevices)
        {
            device.UpdateLeds(leds);
        }
    }

    private static Libinput.LibinputLed MapLeds(KeyboardLeds leds)
    {
        var mapped = default(Libinput.LibinputLed);
        if ((leds & KeyboardLeds.NumLock) != 0)
        {
            mapped |= Libinput.LibinputLed.NumLock;
        }

        if ((leds & KeyboardLeds.CapsLock) != 0)
        {
            mapped |= Libinput.LibinputLed.CapsLock;
        }

        if ((leds & KeyboardLeds.ScrollLock) != 0)
        {
            mapped |= Libinput.LibinputLed.ScrollLock;
        }

        if ((leds & KeyboardLeds.Compose) != 0)
        {
            mapped |= Libinput.LibinputLed.Compose;
        }

        if ((leds & KeyboardLeds.Kana) != 0)
        {
            mapped |= Libinput.LibinputLed.Kana;
        }

        return mapped;
    }

    private void UpdateCapabilities()
    {
        _seat.SetCapability(SeatCapability.Keyboard, _keyboards > 0);
        _seat.SetCapability(SeatCapability.Pointer, _pointers > 0);
        _seat.SetCapability(SeatCapability.Touch, _touchDevices > 0);
        if (_pointers > 0)
        {
            EnsureCursorLoaded();
            _cursor.ShowNamed("left_ptr");
        }
    }

    private void BindParentKeyboard(WaylandKeyboardDevice keyboard)
    {
        _seat.SetCapability(SeatCapability.Keyboard, true);
        if (AdoptParentKeymap)
        {
            keyboard.Keymap += bytes => _seat.Keyboard.SetKeymapFromBuffer(bytes);
        }

        keyboard.Key += (timeMs, key, pressed) =>
        {
            _seat.Keyboard.Activate(null);
            Key?.Invoke(timeMs, key, pressed);
        };
        keyboard.Modifiers += (depressed, latched, locked, group) =>
        {
            _seat.Keyboard.Activate(null);
            _seat.Keyboard.NotifyModifiers(depressed, latched, locked, group);
            ModifiersChanged?.Invoke();
        };
    }

    private void BindParentPointer(WaylandPointerDevice parentPointer)
    {
        _seat.SetCapability(SeatCapability.Pointer, true);
        _cursor.UseParentCursor();
        EnsureCursorLoaded();
        _cursor.AttachParent(parentPointer);

        void MoveTo(uint timeMs, WaylandOutput on, double physicalX, double physicalY)
        {
            var (layoutX, layoutY) = _layout.ToLayout(on, physicalX, physicalY);
            var dx = layoutX - _pointer.X;
            var dy = layoutY - _pointer.Y;
            _pointer.Warp(layoutX, layoutY);
            Motion?.Invoke(timeMs, dx, dy, null, null);
        }

        parentPointer.Enter += (on, x, y) => MoveTo((uint)Environment.TickCount, on, x, y);
        parentPointer.Motion += (timeMs, x, y) =>
        {
            if (_layout.OutputAt(_pointer.X, _pointer.Y) is WaylandOutput on)
            {
                MoveTo(timeMs, on, x, y);
            }
            else if (_layout.Outputs.Count > 0 && _layout.Outputs[0].Output is WaylandOutput first)
            {
                MoveTo(timeMs, first, x, y);
            }
        };
        parentPointer.Leave += () => PointerLeft?.Invoke();
        parentPointer.Button += (timeMs, button, pressed) => Button?.Invoke(timeMs, button, pressed);
        parentPointer.Axis += (timeMs, axis) => Axis?.Invoke(timeMs, axis);
    }

    private void BindParentTouch(WaylandTouchDevice parentTouch)
    {
        _seat.SetCapability(SeatCapability.Touch, true);
        parentTouch.Down += (on, timeMs, slot, physicalX, physicalY) =>
        {
            var (x, y) = _layout.ToLayout(on, physicalX, physicalY);
            TouchDown?.Invoke(timeMs, slot, x, y);
        };
        parentTouch.Motion += (on, timeMs, slot, physicalX, physicalY) =>
        {
            var (x, y) = _layout.ToLayout(on, physicalX, physicalY);
            TouchMotion?.Invoke(timeMs, slot, x, y);
        };
        parentTouch.Up += (timeMs, slot) => TouchUp?.Invoke(timeMs, slot);
        parentTouch.Frame += () => TouchFrame?.Invoke();
        parentTouch.Cancel += () => TouchCancelled?.Invoke();
    }
}
