using Basin.Backend.Drm;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Xkb;

using static Basin.Seat.Backends.SeatBackendsLog;

namespace Basin.Seat.Backends;

/// <summary>
/// Wires libinput or a nested parent's devices to a seat. It counts devices, sets the seat capabilities, loads the cursor, moves the layout pointer and raises each event for the compositor's policy.
/// </summary>
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
    private PointerConstraintDriver? _constraints;
    private readonly bool[] _vtKeys = new bool[VtKeyCount];
    private static readonly uint SwitchVt1 = XkbKeysym.FromName("XF86Switch_VT_1").Value;
    private static readonly uint SwitchVt12 = XkbKeysym.FromName("XF86Switch_VT_12").Value;
    private const int VtKeyCount = 768;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeatBinder"/> class. It creates <see cref="Injector"/> and makes it the seat's synthetic input when the seat has none.
    /// </summary>
    /// <param name="seat">The seat to drive.</param>
    /// <param name="layout">The output layout of the pointer and the touch mapping.</param>
    /// <param name="pointer">The layout pointer that the binder moves.</param>
    /// <param name="cursor">The cursor controller that the binder loads and shows.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
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
        Injector = new SeatInjector(this, seat, layout, pointer) { ThroughBinder = true };
        seat.SyntheticInput ??= Injector;
        seat.Pointer.CursorRequested += request =>
        {
            _clientCursorHidden = request.Surface is null;
            UpdateConfineLock();
        };
    }

    private bool _clientCursorHidden;
    private bool _confineLock;

    /// <summary>
    /// Gets or sets the policy that allows a parent pointer lock for an active confinement with a hidden client cursor. With no policy, every surface is allowed.
    /// </summary>
    public Func<Surface, bool>? ConfineLockAllowed { get; set; }

    /// <summary>
    /// Gets a value indicating whether a confinement holds the parent pointer lock now.
    /// </summary>
    public bool ConfineLocked => _confineLock;

    private bool WantsConfineLock(PointerConstraint? constraint) =>
        constraint is { Kind: ConstraintKind.Confine, IsActive: true } && _clientCursorHidden &&
        (ConfineLockAllowed?.Invoke(constraint.Surface) ?? true);

    private void UpdateConfineLock()
    {
        if (ParentLock is not { } parentLock || _constraints is not { } owner)
        {
            return;
        }

        var wanted = WantsConfineLock(owner.Active);
        if (wanted == _confineLock)
        {
            return;
        }

        if (wanted && _layout.OutputAt(_pointer.X, _pointer.Y) is WaylandOutput output)
        {
            _confineLock = parentLock.Request(owner, output);
        }
        else if (!wanted)
        {
            _confineLock = false;
            parentLock.Release(owner, _pointer.X, _pointer.Y);
        }
    }

    /// <summary>
    /// Gets or sets the DRM backend. When it is set, the cursor images use dumb buffers at the DRM cursor size. Otherwise they use shm buffers of 128 by 128.
    /// </summary>
    public DrmBackend? Drm { get; set; }

    /// <summary>
    /// Gets or sets the cursor theme that gets the cursor images when they load.
    /// </summary>
    public CursorImageTheme? Theme { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the parent's keymap becomes the seat keymap in a nested run. An input configurator that names a keymap wins. The default is <see langword="true"/>.
    /// </summary>
    public bool AdoptParentKeymap { get; set; } = true;

    /// <summary>
    /// Gets or sets the callback that gives the keyboard device with its own keymap for a libinput device. A <see langword="null"/> result means the seat keymap.
    /// </summary>
    public Func<InputDevice, Basin.Capabilities.IInjectedKeyboard?>? KeyboardFor { get; set; }

    /// <summary>
    /// Gets or sets a callback that stops the layout pointer while it returns <see langword="true"/>. Relative motion still goes to the relative pointer manager and raises <see cref="Motion"/>.
    /// </summary>
    public Func<bool>? PointerFrozen { get; set; }

    /// <summary>
    /// Gets or sets the callback that gives the scroll factor of a device. Without it, the factor is 1.
    /// </summary>
    public Func<InputDevice, double>? ScrollFactorFor { get; set; }

    /// <summary>
    /// Gets or sets the callback that names the output of a device. An empty or <see langword="null"/> name falls back to libinput's own mapping.
    /// </summary>
    public Func<InputDevice, string?>? OutputNameFor { get; set; }

    internal InputConfigurator? Configurator { get; set; }

    /// <summary>
    /// Gets or sets the pointer constraint driver that the motion gate asks. A lock or confinement change also requests or releases the parent pointer lock.
    /// </summary>
    public PointerConstraintDriver? Constraints
    {
        get => _constraints;
        set
        {
            if (_constraints is { } previous)
            {
                previous.Changed -= OnConstraintChanged;
            }

            _constraints = value;
            if (value is not null)
            {
                value.Changed += OnConstraintChanged;
            }
        }
    }

    /// <summary>
    /// Gets the parent pointer lock that <see cref="BindParent"/> creates, or <see langword="null"/> before it.
    /// </summary>
    public ParentPointerLock? ParentLock { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the XF86Switch_VT keysyms switch the session on libinput. The default is <see langword="true"/>.
    /// </summary>
    public bool VtSwitching { get; set; } = true;

    /// <summary>
    /// Gets or sets the relative pointer manager that gets every nonzero motion delta before the constraint gate.
    /// </summary>
    public RelativePointerManager? RelativePointer { get; set; }

    /// <summary>
    /// Occurs after libinput adds a device and the seat capabilities are updated.
    /// </summary>
    public event Action<InputDevice>? DeviceAdded;

    /// <summary>
    /// Occurs after libinput removes a device and the seat capabilities are updated.
    /// </summary>
    public event Action<InputDevice>? DeviceRemoved;

    /// <summary>
    /// Occurs for each key from libinput, the parent or the injector. The arguments are the time, the evdev key code and the press state. A VT switch key is not raised.
    /// </summary>
    public event Action<uint, uint, bool>? Key;

    /// <summary>
    /// Occurs after the parent's modifier state is given to the seat keyboard.
    /// </summary>
    public event Action? ModifiersChanged;

    /// <summary>
    /// Occurs after a motion moved the layout pointer. A lock holds the pointer and raises no motion.
    /// </summary>
    public event SeatMotionHandler? Motion;

    /// <summary>
    /// Occurs for each pointer button. The arguments are the time, the evdev button code and the press state.
    /// </summary>
    public event Action<uint, uint, bool>? Button;

    /// <summary>
    /// Occurs for each scroll event, scaled by the device's scroll factor. The arguments are the time and the axis.
    /// </summary>
    public event Action<uint, PointerAxis>? Axis;

    /// <summary>
    /// Occurs when the parent's pointer leaves the nested compositor's window.
    /// </summary>
    public event Action? PointerLeft;

    /// <summary>
    /// Occurs when a contact starts. The arguments are the time, the contact id and the layout position.
    /// </summary>
    public event Action<uint, int, double, double>? TouchDown;

    /// <summary>
    /// Occurs when a contact moves. The arguments are the time, the contact id and the layout position.
    /// </summary>
    public event Action<uint, int, double, double>? TouchMotion;

    /// <summary>
    /// Occurs when a contact ends. The arguments are the time and the contact id.
    /// </summary>
    public event Action<uint, int>? TouchUp;

    /// <summary>
    /// Occurs at the end of a group of touch events that belong together.
    /// </summary>
    public event Action? TouchFrame;

    /// <summary>
    /// Occurs when the source cancels every contact.
    /// </summary>
    public event Action? TouchCancelled;

    /// <summary>
    /// Wires the keys, pointer, scroll, touch and LEDs of a libinput backend to the seat and to the binder events. Device counts set the seat capabilities.
    /// </summary>
    /// <param name="input">The libinput backend.</param>
    /// <param name="start"><see langword="true"/> to start the backend after the wiring.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
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
            ActivateKeyboard(device);
            if (SwitchVt(input.Session, key, pressed))
            {
                return;
            }

            Key?.Invoke(timeMs, key, pressed);
        };
        input.PointerMotion += (_, timeMs, dx, dy, unacceleratedDx, unacceleratedDy) =>
        {
            if (PointerFrozen?.Invoke() == true)
            {
                SendRelative(timeMs, dx, dy, unacceleratedDx, unacceleratedDy);
            }
            else
            {
                var x = _pointer.X + dx;
                var y = _pointer.Y + dy;
                if (!Gate(timeMs, _pointer.X, _pointer.Y, ref x, ref y, unacceleratedDx, unacceleratedDy))
                {
                    return;
                }

                _pointer.Warp(x, y);
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
            var x = _pointer.X;
            var y = _pointer.Y;
            var moves = Gate(timeMs, previousX, previousY, ref x, ref y, null, null);
            if (x != _pointer.X || y != _pointer.Y)
            {
                _pointer.Warp(x, y);
            }

            if (moves)
            {
                Motion?.Invoke(timeMs, _pointer.X - previousX, _pointer.Y - previousY, null, null);
            }
        };
        input.PointerButton += (_, timeMs, button, pressed) => Button?.Invoke(timeMs, button, pressed);
        input.PointerScroll += (device, timeMs, axis) =>
            DeliverAxis(timeMs, axis, ScrollFactorFor is { } factorFor ? factorFor(device) : 1.0);

        WireLibinputTouch(input);

        if (start)
        {
            input.Start();
        }
    }

    /// <summary>
    /// Sends the seat keyboard's LED state to every libinput keyboard, now and on each change.
    /// </summary>
    /// <param name="input">The libinput backend.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Wires only the touch devices of a nested parent. Do not combine it with <see cref="BindParent"/>.
    /// </summary>
    /// <param name="parent">The Wayland backend of the parent.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Wires the keyboards, pointers and touch devices of a nested parent and creates <see cref="ParentLock"/>.
    /// </summary>
    /// <param name="parent">The Wayland backend of the parent.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <see langword="null"/>.</exception>
    public void BindParent(WaylandBackend parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        ParentLock ??= new ParentPointerLock(_layout);
        parent.KeyboardAdded += BindParentKeyboard;
        parent.PointerAdded += BindParentPointer;
        parent.TouchAdded += BindParentTouch;
        parent.KeyboardRemoved += _ => _seat.SetCapability(SeatCapability.Keyboard, _keyboards > 0);
        parent.PointerRemoved += _ => _seat.SetCapability(SeatCapability.Pointer, _pointers > 0);
        parent.TouchRemoved += _ => _seat.SetCapability(SeatCapability.Touch, _touchDevices > 0);
    }

    internal bool InjectTouchDown(uint timeMs, int id, double x, double y)
    {
        if (TouchDown is not { } down)
        {
            return false;
        }

        _seat.SetCapability(SeatCapability.Touch, true);
        down(timeMs, id, x, y);
        return true;
    }

    internal bool InjectTouchMotion(uint timeMs, int id, double x, double y)
    {
        if (TouchMotion is not { } motion)
        {
            return false;
        }

        motion(timeMs, id, x, y);
        return true;
    }

    internal bool InjectTouchUp(uint timeMs, int id)
    {
        if (TouchUp is not { } up)
        {
            return false;
        }

        up(timeMs, id);
        return true;
    }

    internal bool InjectTouchFrame()
    {
        if (TouchFrame is not { } frame)
        {
            return false;
        }

        frame();
        return true;
    }

    internal bool InjectTouchCancel()
    {
        if (TouchCancelled is not { } cancel)
        {
            return false;
        }

        cancel();
        return true;
    }

    /// <summary>
    /// Sets the seat's pointer capability and loads the cursor when the seat has no pointer.
    /// </summary>
    public void EnsurePointerCapability()
    {
        if ((_seat.Capabilities & SeatCapability.Pointer) == 0)
        {
            _seat.SetCapability(SeatCapability.Pointer, true);
            EnsureCursorLoaded();
        }
    }

    /// <summary>
    /// Loads the cursor images once, at the size from <see cref="CursorSizeFromEnvironment"/>. It does nothing when the cursor has images already.
    /// </summary>
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

    /// <summary>
    /// Reads XCURSOR_SIZE.
    /// </summary>
    /// <returns>The positive size from the variable, or 24 when it is unset or not valid.</returns>
    public static int CursorSizeFromEnvironment()
    {
        var text = Environment.GetEnvironmentVariable("XCURSOR_SIZE");
        return int.TryParse(text, out var size) && size > 0 ? size : 24;
    }

    /// <summary>
    /// Makes the keyboard device from <see cref="KeyboardFor"/> the active seat keyboard. A consumer that wires libinput by hand calls it before each key.
    /// </summary>
    /// <param name="device">The libinput device of the key.</param>
    public void ActivateKeyboard(InputDevice device) => _seat.Keyboard.Activate(KeyboardFor?.Invoke(device));

    /// <summary>
    /// Scales a scroll value and its value120 by the device's scroll factor. A consumer that wires libinput by hand calls it on each scroll.
    /// </summary>
    /// <param name="device">The libinput device of the scroll.</param>
    /// <param name="axis">The scroll event.</param>
    /// <returns>The scaled event, or the same event for a factor of 1.</returns>
    public PointerAxis Scale(InputDevice device, PointerAxis axis) =>
        Scaled(axis, ScrollFactorFor is { } factorFor ? factorFor(device) : 1.0);

    internal void DeliverAxis(uint timeMs, PointerAxis axis, double factor) => Axis?.Invoke(timeMs, Scaled(axis, factor));

    /// <summary>
    /// Gets the injector that delivers synthetic input through this binder's events.
    /// </summary>
    public SeatInjector Injector { get; }

    internal void RaiseMotion(uint timeMs, double dx, double dy) => Motion?.Invoke(timeMs, dx, dy, null, null);

    internal void RaiseButton(uint timeMs, uint button, bool pressed) => Button?.Invoke(timeMs, button, pressed);

    internal void RaiseKey(uint timeMs, uint key, bool pressed) => Key?.Invoke(timeMs, key, pressed);

    private static PointerAxis Scaled(PointerAxis axis, double factor) =>
        factor == 1.0
            ? axis
            : axis with
            {
                Value = axis.Value * factor,
                Value120 = (int)Math.Round(axis.Value120 * factor),
            };

    /// <summary>
    /// Finds the output of a device by name, from <see cref="OutputNameFor"/> first and then from libinput.
    /// </summary>
    /// <param name="device">The libinput device.</param>
    /// <returns>The output, or <see langword="null"/> when no name is known or no output has it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public IOutput? OutputFor(InputDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var name = OutputNameFor?.Invoke(device) is { Length: > 0 } mapped ? mapped : device.OutputName;
        if (name is null)
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

    /// <summary>
    /// Finds the output that a touch device maps to. Without a named output, it is the first output of the layout.
    /// </summary>
    /// <param name="device">The libinput touch device.</param>
    /// <returns>The output, or <see langword="null"/> when the layout has none.</returns>
    public IOutput? TouchOutput(InputDevice device)
    {
        if (OutputFor(device) is { } known)
        {
            return known;
        }

        var outputs = _layout.Outputs;
        return outputs.Length > 0 ? outputs[0].Output : null;
    }

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
            keyboard.Keymap += bytes =>
            {
                if (Configurator?.NamesSeatKeymap != true)
                {
                    _seat.Keyboard.SetKeymapFromBuffer(bytes);
                }
            };
        }

        keyboard.RepeatInfo += (rate, delay) => Configurator?.AdoptRepeatInfo(rate, delay);
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
            if (!parentPointer.HasRelativeMotion)
            {
                SendRelative(timeMs, dx, dy, null, null);
            }

            if (_constraints is { IsLocked: true })
            {
                return;
            }

            _constraints?.Constrain(_pointer.X, _pointer.Y, ref layoutX, ref layoutY);
            _pointer.Warp(layoutX, layoutY);
            Motion?.Invoke(timeMs, dx, dy, null, null);
        }

        bool Relative() => parentPointer.HasRelativeMotion &&
            (PointerFrozen?.Invoke() == true || ParentLock?.IsLocked == true);

        parentPointer.Enter += (on, x, y) =>
        {
            if (!Relative())
            {
                MoveTo((uint)Environment.TickCount, on, x, y);
            }
        };
        parentPointer.RelativeMotion += (microseconds, dx, dy, unacceleratedDx, unacceleratedDy) =>
        {
            var timeMs = (uint)(microseconds / 1000);
            SendRelative(timeMs, dx, dy, unacceleratedDx, unacceleratedDy);
            if (_confineLock && ParentLock?.IsLocked == true && _constraints is { Active.Kind: ConstraintKind.Confine } confining)
            {
                var targetX = _pointer.X + dx;
                var targetY = _pointer.Y + dy;
                confining.Constrain(_pointer.X, _pointer.Y, ref targetX, ref targetY);
                _pointer.Warp(targetX, targetY);
                Motion?.Invoke(timeMs, dx, dy, unacceleratedDx, unacceleratedDy);
                return;
            }

            if (Relative() && _constraints is not { IsLocked: true })
            {
                Motion?.Invoke(timeMs, dx, dy, unacceleratedDx, unacceleratedDy);
            }
        };
        parentPointer.Motion += (timeMs, x, y) =>
        {
            if (Relative())
            {
                return;
            }

            if (_layout.OutputAt(_pointer.X, _pointer.Y) is WaylandOutput on)
            {
                MoveTo(timeMs, on, x, y);
            }
            else if (_layout.Outputs is { Length: > 0 } outputs && outputs[0].Output is WaylandOutput first)
            {
                MoveTo(timeMs, first, x, y);
            }
        };
        parentPointer.Leave += () => PointerLeft?.Invoke();
        parentPointer.Button += (timeMs, button, pressed) => Button?.Invoke(timeMs, button, pressed);
        parentPointer.Axis += (timeMs, axis) => Axis?.Invoke(timeMs, axis);
    }

    internal bool Gate(uint timeMs, double fromX, double fromY, ref double x, ref double y, double? unacceleratedDx, double? unacceleratedDy)
    {
        SendRelative(timeMs, x - fromX, y - fromY, unacceleratedDx, unacceleratedDy);
        if (_constraints is not { } constraints)
        {
            return true;
        }

        constraints.Constrain(fromX, fromY, ref x, ref y);
        return !constraints.IsLocked;
    }

    private bool SwitchVt(Basin.Session.ISession session, uint key, bool pressed)
    {
        if (key >= VtKeyCount)
        {
            return false;
        }

        if (!pressed)
        {
            var held = _vtKeys[key];
            _vtKeys[key] = false;
            return held;
        }

        if (!VtSwitching)
        {
            return false;
        }

        var symbol = _seat.Keyboard.KeysymFor(key).Value;
        if (symbol < SwitchVt1 || symbol > SwitchVt12)
        {
            return false;
        }

        _vtKeys[key] = true;
        try
        {
            session.SwitchSession((int)(symbol - SwitchVt1 + 1));
        }
        catch (NotSupportedException)
        {
            Log.Debug($"vt switch: no seat manager to switch sessions");
        }

        return true;
    }

    private void SendRelative(uint timeMs, double dx, double dy, double? unacceleratedDx, double? unacceleratedDy)
    {
        if (RelativePointer is null || (dx == 0 && dy == 0))
        {
            return;
        }

        RelativePointer.NotifyMotion((ulong)timeMs * 1000, dx, dy, unacceleratedDx ?? dx, unacceleratedDy ?? dy);
    }

    private void OnConstraintChanged(PointerConstraint? previous, PointerConstraint? next)
    {
        var releaseX = _pointer.X;
        var releaseY = _pointer.Y;
        if (previous is not null && _constraints is { } constraints &&
            constraints.ReleasePoint(previous, out var hintX, out var hintY))
        {
            _pointer.Warp(hintX, hintY);
            (releaseX, releaseY) = (_pointer.X, _pointer.Y);
        }

        if (ParentLock is not { } parentLock || _constraints is not { } owner)
        {
            return;
        }

        var confine = WantsConfineLock(next);
        if ((next is { Kind: ConstraintKind.Lock } || confine) &&
            _layout.OutputAt(_pointer.X, _pointer.Y) is WaylandOutput output)
        {
            var granted = parentLock.Request(owner, output);
            _confineLock = confine && granted;
        }
        else
        {
            _confineLock = false;
            parentLock.Release(owner, releaseX, releaseY);
        }
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
