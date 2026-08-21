using Wayland;
using Basin.Capabilities;
using Xkb;

namespace Basin.Seat;

public sealed class SeatKeyboard : IDisposable
{
    private readonly Seat _seat;
    private readonly List<IKeyboardGrab> _grabs = [];
    private readonly DefaultGrab _defaultGrab;
    private readonly List<uint> _pressedKeys = [];
    private readonly XkbKeymapSource _keymapSource = new();
    private readonly KeyboardDevice _default;
    private readonly List<KeyboardDevice> _devices = [];
    private KeyboardDevice _active;
    private Capabilities.Keymap? _broadcast;
    private IKeymapSource? _externalSource;
    private (uint Depressed, uint Latched, uint Locked, uint Group) _modifiers;
    private int _repeatRate = 25;
    private int _repeatDelay = 400;

    internal SeatKeyboard(Seat seat)
    {
        _seat = seat;
        _defaultGrab = new DefaultGrab(this);
        _default = new KeyboardDevice(this, isDefault: true);
        _devices.Add(_default);
        _active = _default;
    }

    public Surface? Focus { get; private set; }

    public XkbKeymap? Keymap => _active.Compiled;

    public XkbState? State => _active.State;

    public (int Fd, uint Size)? KeymapBuffer => _active.File is { } file ? (file.Fd, file.Size) : null;

    public (int Fd, uint Size)? KeymapFor(Wayland.Server.WlClient? client) =>
        _active.File is { } file ? (file.FdFor(client), file.Size) : null;

    public (uint Depressed, uint Latched, uint Locked, uint Group) ModifierState => _modifiers;

    public (int Rate, int Delay) RepeatInfo => (_repeatRate, _repeatDelay);

    public IReadOnlyList<uint> PressedKeys => _pressedKeys;

    public IKeyboardGrab Grab => _grabs.Count > 0 ? _grabs[^1] : _defaultGrab;

    public bool HasGrab => _grabs.Count > 0;

    public event Action? ModifiersChanged;

    public event Action<Surface?>? FocusChanged;

    public event Action? KeymapChanged;

    public IKeymapSource KeymapSource
    {
        get => _externalSource ?? _keymapSource;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _externalSource = ReferenceEquals(value, _keymapSource) ? null : value;
        }
    }

    public void SetKeymap(in KeymapNames names = default)
    {
        if (KeymapSource.TryCompile(names, out var keymap))
        {
            ApplyDeviceKeymap(_default, keymap, SourceCompiled());
        }
    }

    public void SetKeymapFromHost()
    {
        if (KeymapSource is HostKeymapSource host && host.TryCompile(out var keymap))
        {
            ApplyDeviceKeymap(_default, keymap, host.LastCompiled);
        }
    }

    private XkbKeymap? SourceCompiled() => KeymapSource switch
    {
        XkbKeymapSource xkb => xkb.LastCompiled,
        HostKeymapSource host => host.LastCompiled,
        _ => null,
    };

    public void SetKeymapFromBuffer(ReadOnlySpan<byte> keymapText)
    {
        if (KeymapSource.TryCompile(System.Text.Encoding.UTF8.GetString(keymapText), out var keymap))
        {
            ApplyDeviceKeymap(_default, keymap, SourceCompiled());
        }
    }

    public KeyboardDevice CreateDevice()
    {
        var device = new KeyboardDevice(this, isDefault: false);
        _devices.Add(device);
        return device;
    }

    public void Activate(Capabilities.IInjectedKeyboard? keyboard)
    {
        var device = keyboard as KeyboardDevice;
        if (device is null || device.File is null || !_devices.Contains(device))
        {
            device = _default;
        }

        if (ReferenceEquals(_active, device))
        {
            return;
        }

        _active = device;
        if (device.File is { } file && !ReferenceEquals(file, _broadcast))
        {
            BroadcastKeymap(file);
            _modifiers = device.Modifiers;
            Grab.Modifiers();
            ModifiersChanged?.Invoke();
            return;
        }

        DeliverModifiers(device.Modifiers);
    }

    internal bool SetDeviceKeymap(KeyboardDevice device, ReadOnlySpan<byte> keymapText)
    {
        if (device.IsDefault)
        {
            var before = _default.File;
            SetKeymapFromBuffer(keymapText);
            return !ReferenceEquals(before, _default.File);
        }

        if (device.OwnSource is not { } source ||
            !source.TryCompile(System.Text.Encoding.UTF8.GetString(keymapText), out var keymap))
        {
            return false;
        }

        ApplyDeviceKeymap(device, keymap, source.LastCompiled);
        return true;
    }

    internal void RemoveDevice(KeyboardDevice device)
    {
        if (device.IsDefault || !_devices.Remove(device))
        {
            return;
        }

        if (ReferenceEquals(_active, device))
        {
            _active = _default;
            if (_default.File is { } file && !ReferenceEquals(file, _broadcast))
            {
                BroadcastKeymap(file);
            }

            DeliverModifiers(_default.Modifiers);
        }

        device.Teardown();
    }

    public void SetRepeatInfo(int rate, int delay)
    {
        _repeatRate = rate;
        _repeatDelay = delay;
        ForEachKeyboard(static (keyboard, state) =>
        {
            if (keyboard.Version >= 4)
            {
                keyboard.SendRepeatInfo(state._repeatRate, state._repeatDelay);
            }
        });
    }

    public void NotifyEnter(Surface? surface) => Grab.Enter(surface, CollectPressed());

    public void NotifyClearFocus() => Grab.Enter(null, default);

    public void NotifyKey(uint timeMs, uint key, bool pressed) =>
        NotifyKey(timeMs, key, pressed ? WlKeyboard.KeyState.Pressed : WlKeyboard.KeyState.Released);

    public void NotifyKey(uint timeMs, uint key, WlKeyboard.KeyState state)
    {
        TrackKey(key, state);
        Grab.Key(timeMs, key, state);
        UpdateKeyState(key, state);
    }

    public void NotifyKeyConsumed(uint key, bool pressed) =>
        NotifyKeyConsumed(key, pressed ? WlKeyboard.KeyState.Pressed : WlKeyboard.KeyState.Released);

    public void NotifyKeyConsumed(uint key, WlKeyboard.KeyState state)
    {
        TrackKey(key, state);
        UpdateKeyState(key, state);
    }

    public void NotifyModifiers(uint depressed, uint latched, uint locked, uint group)
    {
        _active.State?.UpdateMask(depressed, latched, locked, 0, 0, group);
        _active.Modifiers = (depressed, latched, locked, group);
        DeliverModifiers(_active.Modifiers);
    }

    public XkbKeysym KeysymFor(uint key) => State?.GetKeyOneSym(key + 8) ?? default;

    public void StartGrab(IKeyboardGrab grab)
    {
        _grabs.Add(grab);
        grab.Enter(Focus, CollectPressed());
    }

    public void EndGrab(IKeyboardGrab grab)
    {
        var wasActive = HasGrab && Grab == grab;
        _grabs.Remove(grab);
        if (wasActive)
        {
            grab.Cancel();
        }
    }

    public void SendEnter(Surface? surface, ReadOnlySpan<uint> pressedKeys)
    {
        if (Focus == surface)
        {
            return;
        }

        if (Focus is { } old && !old.IsDestroyed && _seat.ClientOf(old) is { } oldClient)
        {
            var leaveSerial = _seat.NextSerial(SerialKind.Other);
            foreach (var keyboard in oldClient.Keyboards)
            {
                keyboard.SendLeave(leaveSerial, old.Resource);
            }
        }

        Focus = surface;
        if (surface is not null && _seat.ClientOf(surface) is { } client)
        {
            var serial = _seat.NextSerial(SerialKind.KeyboardEnter);
            Span<byte> keys = pressedKeys.Length <= 16 ? stackalloc byte[pressedKeys.Length * 4] : new byte[pressedKeys.Length * 4];
            for (var i = 0; i < pressedKeys.Length; i++)
            {
                BitConverter.TryWriteBytes(keys[(i * 4)..], pressedKeys[i]);
            }

            foreach (var keyboard in client.Keyboards)
            {
                keyboard.SendEnter(serial, surface.Resource, keys);
            }

            SendModifiersTo(client);
            _seat.DataDevice.OnKeyboardFocus(surface);
        }

        FocusChanged?.Invoke(surface);
    }

    public uint SendKey(uint timeMs, uint key, WlKeyboard.KeyState state)
    {
        var serial = _seat.NextSerial(state == WlKeyboard.KeyState.Pressed ? SerialKind.KeyPress : SerialKind.KeyRelease);
        if (_seat.ClientOf(Focus) is { } client)
        {
            foreach (var keyboard in client.Keyboards)
            {
                keyboard.SendKey(serial, timeMs, key, state);
            }
        }

        return serial;
    }

    public void SendModifiers()
    {
        if (_seat.ClientOf(Focus) is { } client)
        {
            SendModifiersTo(client);
        }
    }

    public void Dispose()
    {
        foreach (var device in _devices)
        {
            device.Teardown();
        }

        _devices.Clear();
        _keymapSource.Dispose();
    }

    internal void InitializeResource(WlKeyboardResource keyboard)
    {
        if (_broadcast is { } keymap)
        {
            keyboard.SendKeymap(WlKeyboard.KeymapFormat.XkbV1, keymap.FdFor(keyboard.Client), keymap.Size);
        }

        if (keyboard.Version >= 4)
        {
            keyboard.SendRepeatInfo(_repeatRate, _repeatDelay);
        }
    }

    private void ApplyDeviceKeymap(KeyboardDevice device, Capabilities.Keymap file, XkbKeymap? compiled)
    {
        var oldState = device.State;
        var oldFile = device.File;
        device.Compiled = compiled;
        device.State = compiled?.CreateState();
        device.File = file;
        device.Modifiers = default;
        if (ReferenceEquals(device, _active))
        {
            BroadcastKeymap(file);
            _modifiers = device.Modifiers;
            Grab.Modifiers();
            ModifiersChanged?.Invoke();
        }

        oldState?.Dispose();
        oldFile?.Dispose();
    }

    private void BroadcastKeymap(Capabilities.Keymap file)
    {
        _broadcast = file;
        ForEachKeyboard(static (keyboard, self) =>
            keyboard.SendKeymap(WlKeyboard.KeymapFormat.XkbV1, self._broadcast!.FdFor(keyboard.Client), self._broadcast.Size));
        KeymapChanged?.Invoke();
    }

    private void DeliverModifiers((uint Depressed, uint Latched, uint Locked, uint Group) modifiers)
    {
        var changed = _modifiers != modifiers;
        _modifiers = modifiers;
        if (changed)
        {
            Grab.Modifiers();
            ModifiersChanged?.Invoke();
        }
    }

    private void RefreshModifiers()
    {
        if (_active.State is not { } state)
        {
            return;
        }

        var modifiers = (
            state.SerializeMods(XkbStateComponent.ModsDepressed),
            state.SerializeMods(XkbStateComponent.ModsLatched),
            state.SerializeMods(XkbStateComponent.ModsLocked),
            state.SerializeLayout(XkbStateComponent.LayoutEffective));
        _active.Modifiers = modifiers;
        DeliverModifiers(modifiers);
    }

    private void SendModifiersTo(SeatClient client)
    {
        var serial = _seat.NextSerial(SerialKind.Other);
        foreach (var keyboard in client.Keyboards)
        {
            keyboard.SendModifiers(serial, _modifiers.Depressed, _modifiers.Latched, _modifiers.Locked, _modifiers.Group);
        }
    }

    private void UpdateKeyState(uint key, WlKeyboard.KeyState state)
    {
        if (_active.State is not { } xkb)
        {
            return;
        }

        xkb.UpdateKey(key + 8, state == WlKeyboard.KeyState.Pressed ? XkbKeyDirection.Down : XkbKeyDirection.Up);
        RefreshModifiers();
    }

    private void TrackKey(uint key, WlKeyboard.KeyState state)
    {
        if (state == WlKeyboard.KeyState.Pressed)
        {
            if (!_pressedKeys.Contains(key))
            {
                _pressedKeys.Add(key);
            }
        }
        else
        {
            _pressedKeys.Remove(key);
        }
    }

    private uint[] CollectPressed() => _pressedKeys.ToArray();

    private void ForEachKeyboard(Action<WlKeyboardResource, SeatKeyboard> action)
    {
        foreach (var surfaceClient in AllClients())
        {
            foreach (var keyboard in surfaceClient.Keyboards)
            {
                action(keyboard, this);
            }
        }
    }

    private IEnumerable<SeatClient> AllClients() => _seat.Clients;

    private sealed class DefaultGrab(SeatKeyboard keyboard) : IKeyboardGrab
    {
        public void Enter(Surface? surface, ReadOnlySpan<uint> pressedKeys) => keyboard.SendEnter(surface, pressedKeys);

        public void Key(uint timeMs, uint key, WlKeyboard.KeyState state) => keyboard.SendKey(timeMs, key, state);

        public void Modifiers() => keyboard.SendModifiers();

        public void Cancel()
        {
        }
    }
}
