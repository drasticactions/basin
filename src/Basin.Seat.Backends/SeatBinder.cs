using Basin.Backend.Drm;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Xkb;

using static Basin.Seat.Backends.SeatBackendsLog;

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
    private PointerConstraintDriver? _constraints;
    private readonly bool[] _vtKeys = new bool[VtKeyCount];
    private static readonly uint SwitchVt1 = XkbKeysym.FromName("XF86Switch_VT_1").Value;
    private static readonly uint SwitchVt12 = XkbKeysym.FromName("XF86Switch_VT_12").Value;
    private const int VtKeyCount = 768;

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

    public Func<Surface, bool>? ConfineLockAllowed { get; set; }

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

    public DrmBackend? Drm { get; set; }

    public CursorImageTheme? Theme { get; set; }

    public bool AdoptParentKeymap { get; set; } = true;

    public Func<InputDevice, Basin.Capabilities.IInjectedKeyboard?>? KeyboardFor { get; set; }

    public Func<bool>? PointerFrozen { get; set; }

    public Func<InputDevice, double>? ScrollFactorFor { get; set; }

    public Func<InputDevice, string?>? OutputNameFor { get; set; }

    internal InputConfigurator? Configurator { get; set; }

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

    public ParentPointerLock? ParentLock { get; private set; }

    public bool VtSwitching { get; set; } = true;

    public RelativePointerManager? RelativePointer { get; set; }

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

    public void ActivateKeyboard(InputDevice device) => _seat.Keyboard.Activate(KeyboardFor?.Invoke(device));

    public PointerAxis Scale(InputDevice device, PointerAxis axis) =>
        Scaled(axis, ScrollFactorFor is { } factorFor ? factorFor(device) : 1.0);

    internal void DeliverAxis(uint timeMs, PointerAxis axis, double factor) => Axis?.Invoke(timeMs, Scaled(axis, factor));

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
            Log.Debug($"no seat manager to switch sessions");
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
