using System.Text;
using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;
using Xkb;

namespace Basin.Seat;

/// <summary>
/// The seat's keyboard. It tracks the xkb state, delivers enter, leave, key, modifier and keymap events to the focused client, and runs keyboard grabs.
/// </summary>
public sealed class SeatKeyboard : Capabilities.IActiveKeymap, Capabilities.IKeymapLookup, Capabilities.IKeyText, Capabilities.IKeymapOverride, IKeySink, IDisposable
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
    private KeyboardDevice? _repeatDevice;
    private (int Rate, int Delay) _sentRepeat = (25, 400);
    private XkbContext? _composeContext;
    private XkbComposeState? _compose;
    private bool _composeLoaded;

    internal SeatKeyboard(Seat seat)
    {
        _seat = seat;
        _defaultGrab = new DefaultGrab(this);
        _default = new KeyboardDevice(this, isDefault: true);
        _devices.Add(_default);
        _active = _default;
    }

    /// <summary>
    /// Gets the surface that has keyboard focus, or <see langword="null"/>.
    /// </summary>
    public Surface? Focus { get; private set; }

    /// <summary>
    /// Gets the compiled keymap of the active device. It is null when no keymap is set or the source produced text only.
    /// </summary>
    public XkbKeymap? Keymap => _active.Compiled;

    /// <summary>
    /// Gets the xkb state of the active device, or <see langword="null"/> when it has no compiled keymap.
    /// </summary>
    public XkbState? State => _active.State;

    /// <inheritdoc/>
    public (int Fd, uint Size)? KeymapBuffer => _active.File is { } file ? (file.Fd, file.Size) : null;

    /// <summary>
    /// Gets the keymap of the active device as a descriptor for one client to map.
    /// </summary>
    /// <param name="client">The client, or <see langword="null"/>.</param>
    /// <returns>The descriptor and the size in bytes, or <see langword="null"/> when no keymap is set.</returns>
    public (int Fd, uint Size)? KeymapFor(Wayland.Server.WlClient? client) =>
        _active.File is { } file ? (file.FdFor(client), file.Size) : null;

    /// <summary>
    /// Gets the modifier masks and the effective layout that clients received last.
    /// </summary>
    public (uint Depressed, uint Latched, uint Locked, uint Group) ModifierState => _modifiers;

    /// <summary>
    /// Gets the repeat rate in keys per second and the delay in milliseconds that clients received last. A device can replace the seat values.
    /// </summary>
    public (int Rate, int Delay) RepeatInfo => _sentRepeat;

    /// <summary>
    /// Gets the seat repeat rate in keys per second and the delay in milliseconds that <see cref="SetRepeatInfo"/> set.
    /// </summary>
    public (int Rate, int Delay) SeatRepeatInfo => (_repeatRate, _repeatDelay);

    /// <summary>
    /// Gets the evdev keycodes held now, including keys that a binding consumed.
    /// </summary>
    public IReadOnlyList<uint> PressedKeys => _pressedKeys;

    /// <summary>
    /// Gets the grab that receives the notify calls: the newest started grab, or the default grab that delivers to the focus.
    /// </summary>
    public IKeyboardGrab Grab => _grabs.Count > 0 ? _grabs[^1] : _defaultGrab;

    /// <summary>
    /// Gets a value indicating whether a grab other than the default one is active.
    /// </summary>
    public bool HasGrab => _grabs.Count > 0;

    /// <summary>
    /// Occurs after the modifier state or the effective layout changes. Read <see cref="ModifierState"/> in the handler.
    /// </summary>
    public event Action? ModifiersChanged;

    /// <summary>
    /// Gets the LEDs that the active xkb state lights.
    /// </summary>
    public KeyboardLeds Leds { get; private set; }

    /// <summary>
    /// Occurs after <see cref="Leds"/> changes.
    /// </summary>
    public event Action? LedsChanged;

    /// <summary>
    /// Occurs after the keyboard focus changes, with the new focus or <see langword="null"/>.
    /// </summary>
    public event Action<Surface?>? FocusChanged;

    /// <inheritdoc/>
    public event Action? KeymapChanged;

    /// <summary>
    /// Gets or sets the source that compiles the seat keymap. A set compiles nothing. Call <see cref="SetKeymap"/> or another set method after it.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public IKeymapSource KeymapSource
    {
        get => _externalSource ?? _keymapSource;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _externalSource = ReferenceEquals(value, _keymapSource) ? null : value;
        }
    }

    /// <summary>
    /// Compiles the seat keymap from XKB rule names and sends it to clients. A failed compile keeps the old keymap.
    /// </summary>
    /// <param name="names">The rule names. The default value uses the xkb defaults.</param>
    public void SetKeymap(in KeymapNames names = default)
    {
        if (KeymapSource.TryCompile(names, out var keymap))
        {
            ApplyDeviceKeymap(_default, keymap, SourceCompiled());
        }
    }

    /// <summary>
    /// Compiles the seat keymap from the host when <see cref="KeymapSource"/> is a <see cref="HostKeymapSource"/>, and sends it to clients. Otherwise it does nothing.
    /// </summary>
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

    /// <summary>
    /// Compiles the seat keymap from XKB text and sends it to clients. A failed compile keeps the old keymap.
    /// </summary>
    /// <param name="keymapText">The keymap text in UTF-8.</param>
    public void SetKeymapFromBuffer(ReadOnlySpan<byte> keymapText) =>
        ApplyText(_default, System.Text.Encoding.UTF8.GetString(keymapText));

    private bool ApplyText(KeyboardDevice device, string text)
    {
        if (device.IsDefault)
        {
            if (!KeymapSource.TryCompile(text, out var keymap))
            {
                return false;
            }

            ApplyDeviceKeymap(device, keymap, SourceCompiled());
            return true;
        }

        if (device.OwnSource is not { } source || !source.TryCompile(text, out var own))
        {
            return false;
        }

        ApplyDeviceKeymap(device, own, source.LastCompiled);
        return true;
    }

    private bool IsLive(KeyboardDevice device) => device.IsDefault || _devices.Contains(device);

    /// <summary>
    /// Creates a virtual keyboard device that can carry its own keymap and repeat rate.
    /// </summary>
    /// <returns>The device. Dispose it to remove it.</returns>
    public KeyboardDevice CreateDevice()
    {
        var device = new KeyboardDevice(this, isDefault: false);
        _devices.Add(device);
        return device;
    }

    /// <summary>
    /// Makes a device the source of the next key events. Clients receive its keymap and modifiers when they differ from the current ones.
    /// </summary>
    /// <param name="keyboard">A device of this keyboard, or <see langword="null"/> for the seat keymap. A device with no keymap uses the seat keymap.</param>
    public void Activate(Capabilities.IInjectedKeyboard? keyboard)
    {
        var device = keyboard as KeyboardDevice;
        if (device is not null && !_devices.Contains(device))
        {
            device = null;
        }

        if (!ReferenceEquals(_repeatDevice, device))
        {
            _repeatDevice = device;
            RefreshRepeat();
        }

        if (device is null || device.File is null)
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
            UpdateLeds();
            Grab.Modifiers();
            AllocationScope.Pause();
            try
            {
                ModifiersChanged?.Invoke();
            }
            finally
            {
                AllocationScope.Resume();
            }
            return;
        }

        DeliverModifiers(device.Modifiers);
    }

    internal bool SetDeviceKeymap(KeyboardDevice device, ReadOnlySpan<byte> keymapText) =>
        ApplyText(device, System.Text.Encoding.UTF8.GetString(keymapText));

    internal bool SetDeviceKeymap(KeyboardDevice device, in KeymapNames names)
    {
        if (device.IsDefault)
        {
            var before = _default.File;
            SetKeymap(names);
            return !ReferenceEquals(before, _default.File);
        }

        if (device.OwnSource is not { } source || !source.TryCompile(names, out var keymap))
        {
            return false;
        }

        ApplyDeviceKeymap(device, keymap, source.LastCompiled);
        return true;
    }

    internal void ClearDeviceKeymap(KeyboardDevice device)
    {
        if (device.IsDefault || device.File is null)
        {
            return;
        }

        var wasActive = ReferenceEquals(_active, device);
        var oldState = device.State;
        var oldFile = device.File;
        device.State = null;
        device.File = null;
        device.Compiled = null;
        device.Modifiers = default;
        device.LedIndices = default;
        if (wasActive)
        {
            _active = _default;
            if (_default.File is { } file && !ReferenceEquals(file, _broadcast))
            {
                BroadcastKeymap(file);
            }

            DeliverModifiers(_default.Modifiers);
        }

        if (ReferenceEquals(_broadcast, oldFile))
        {
            _broadcast = _active.File;
        }

        oldState?.Dispose();
        oldFile?.Dispose();
    }

    internal void RefreshRepeat()
    {
        var repeat = _repeatDevice?.Repeat ?? (_repeatRate, _repeatDelay);
        if (repeat == _sentRepeat)
        {
            return;
        }

        _sentRepeat = repeat;
        ForEachKeyboard(static (keyboard, state) =>
        {
            if (keyboard.Version >= 4)
            {
                keyboard.SendRepeatInfo(state._sentRepeat.Rate, state._sentRepeat.Delay);
            }
        });
    }

    internal void RemoveDevice(KeyboardDevice device)
    {
        if (device.IsDefault || !_devices.Remove(device))
        {
            return;
        }

        if (ReferenceEquals(_repeatDevice, device))
        {
            _repeatDevice = null;
            RefreshRepeat();
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

        if (device.File is not null && ReferenceEquals(_broadcast, device.File))
        {
            _broadcast = _active.File;
        }

        device.Teardown();
    }

    /// <summary>
    /// Sets the seat repeat rate and sends it to clients at version 4 and later, unless the active device has its own rate.
    /// </summary>
    /// <param name="rate">The repeat rate in keys per second. Zero turns repeat off.</param>
    /// <param name="delay">The delay in milliseconds before repeat starts.</param>
    public void SetRepeatInfo(int rate, int delay)
    {
        _repeatRate = rate;
        _repeatDelay = delay;
        if (_repeatDevice?.Repeat is null)
        {
            _sentRepeat = (rate, delay);
            ForEachKeyboard(static (keyboard, state) =>
            {
                if (keyboard.Version >= 4)
                {
                    keyboard.SendRepeatInfo(state._sentRepeat.Rate, state._sentRepeat.Delay);
                }
            });
        }
    }

    /// <summary>
    /// Passes a focus change to the active grab, with the held keys that the client can see.
    /// </summary>
    /// <param name="surface">The new focus, or <see langword="null"/>.</param>
    public void NotifyEnter(Surface? surface) => Grab.Enter(surface, CollectPressed());

    /// <summary>
    /// Passes a focus clear to the active grab.
    /// </summary>
    public void NotifyClearFocus() => Grab.Enter(null, default);

    /// <summary>
    /// Delivers a key through the active grab and updates the xkb state. The release of a consumed key updates the state only.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void NotifyKey(uint timeMs, uint key, bool pressed) =>
        NotifyKey(timeMs, key, pressed ? WlKeyboard.KeyState.Pressed : WlKeyboard.KeyState.Released);

    private int _scopedNotifies;

    private const int NotifyScopeWarmup = 10;

    /// <summary>
    /// Delivers a key through the active grab and updates the xkb state, and tells whether the key went to the grab.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns><see langword="false"/> when the key is the release of a consumed press, which updates the state only.</returns>
    public bool TryNotifyKey(uint timeMs, uint key, bool pressed)
    {
        if (pressed)
        {
            ForgetConsumed(key);
        }
        else if (ForgetConsumed(key))
        {
            TrackKey(key, WlKeyboard.KeyState.Released);
            UpdateKeyState(key, WlKeyboard.KeyState.Released);
            return false;
        }

        DeliverKey(timeMs, key, pressed ? WlKeyboard.KeyState.Pressed : WlKeyboard.KeyState.Released);
        return true;
    }

    /// <summary>
    /// Checks whether the held press of a key was consumed by a binding, so that a consumer can run a binding on the release.
    /// </summary>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <returns><see langword="true"/> while the consumed press is held.</returns>
    public bool WasConsumed(uint key)
    {
        for (var i = 0; i < _consumedCount; i++)
        {
            if (_consumedKeys[i] == key)
            {
                return true;
            }
        }

        return false;
    }

    private readonly uint[] _consumedKeys = new uint[16];

    private int _consumedCount;

    private void RememberConsumed(uint key)
    {
        if (WasConsumed(key) || _consumedCount == _consumedKeys.Length)
        {
            return;
        }

        _consumedKeys[_consumedCount++] = key;
    }

    private bool ForgetConsumed(uint key)
    {
        for (var i = 0; i < _consumedCount; i++)
        {
            if (_consumedKeys[i] == key)
            {
                _consumedKeys[i] = _consumedKeys[--_consumedCount];
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Delivers a key through the active grab and updates the xkb state. The release of a consumed key updates the state only.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <param name="state">The key state.</param>
    public void NotifyKey(uint timeMs, uint key, WlKeyboard.KeyState state) =>
        _ = TryNotifyKey(timeMs, key, state == WlKeyboard.KeyState.Pressed);

    private void DeliverKey(uint timeMs, uint key, WlKeyboard.KeyState state)
    {
        if (HasGrab || _scopedNotifies < NotifyScopeWarmup)
        {
            _scopedNotifies += HasGrab ? 0 : 1;
            TrackKey(key, state);
            Grab.Key(timeMs, key, state);
            UpdateKeyState(key, state);
            return;
        }

        AllocationScope.Begin(region: "KeyboardNotify", forgiving: true);
        try
        {
            TrackKey(key, state);
            _defaultGrab.Key(timeMs, key, state);
            UpdateKeyState(key, state);
        }
        finally
        {
            AllocationScope.End();
        }
    }

    /// <summary>
    /// Tracks a key that a compositor binding took and updates the modifiers, with no delivery. The release of a press that the client saw is delivered.
    /// </summary>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void NotifyKeyConsumed(uint key, bool pressed) =>
        NotifyKeyConsumed(key, pressed ? WlKeyboard.KeyState.Pressed : WlKeyboard.KeyState.Released);

    /// <summary>
    /// Tracks a key that a compositor binding took and updates the modifiers, with no delivery. At most 16 consumed presses are remembered.
    /// </summary>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <param name="state">The key state. A release of a press that the client saw is delivered.</param>
    public void NotifyKeyConsumed(uint key, WlKeyboard.KeyState state)
    {
        if (state == WlKeyboard.KeyState.Pressed)
        {
            RememberConsumed(key);
            TrackKey(key, state);
            UpdateKeyState(key, state);
            return;
        }

        if (!ForgetConsumed(key) && _pressedKeys.Contains(key))
        {
            SeatLog.Log.Debug($"keyboard: key {key} press reached the client, its consumed release is delivered too");
            DeliverKey((uint)Environment.TickCount64, key, state);
            return;
        }

        TrackKey(key, state);
        UpdateKeyState(key, state);
    }

    /// <summary>
    /// Sets the modifier state of the active device from an outside source, for example a nested parent, and delivers it when it changed.
    /// </summary>
    /// <param name="depressed">The depressed modifier mask.</param>
    /// <param name="latched">The latched modifier mask.</param>
    /// <param name="locked">The locked modifier mask.</param>
    /// <param name="group">The effective layout index.</param>
    public void NotifyModifiers(uint depressed, uint latched, uint locked, uint group)
    {
        _active.State?.UpdateMask(depressed, latched, locked, 0, 0, group);
        _active.Modifiers = (depressed, latched, locked, group);
        DeliverModifiers(_active.Modifiers);
    }

    /// <summary>
    /// Gets the keysym that a key produces in the current state, with the modifiers applied.
    /// </summary>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <returns>The keysym, or the default value when there is no compiled keymap or the key produces more than one keysym.</returns>
    public XkbKeysym KeysymFor(uint key) => State?.GetKeyOneSym(key + 8) ?? default;

    /// <inheritdoc/>
    public uint KeysymForKeycode(uint keycode) => KeysymFor(keycode).Value;

    /// <inheritdoc/>
    public uint RawKeysymForKeycode(uint keycode) => RawKeysymFor(keycode).Value;

    /// <summary>
    /// Gets the keysym of a key at level 1 in the effective layout, as if no modifier were held.
    /// </summary>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <returns>The keysym, or the default value when there is no compiled keymap or the key has no keysym.</returns>
    public XkbKeysym RawKeysymFor(uint key)
    {
        if (Keymap is not { } keymap || State is not { } state)
        {
            return default;
        }

        var layout = state.SerializeLayout(XkbStateComponent.LayoutEffective);
        var syms = keymap.GetKeySymsByLevel(key + 8, layout, 0);
        return syms.Length > 0 ? syms[0] : default;
    }

    /// <inheritdoc/>
    public bool TryKeycodeForKeysym(uint keysym, out uint keycode, out uint modifiers)
    {
        keycode = 0;
        modifiers = 0;
        if (Keymap is not { } keymap || State is not { } state)
        {
            return false;
        }

        var layout = state.SerializeLayout(XkbStateComponent.LayoutEffective);
        foreach (var candidate in keymap.GetKeycodes())
        {
            var levels = keymap.GetNumLevelsForKey(candidate, layout);
            for (var level = 0u; level < levels; level++)
            {
                var syms = keymap.GetKeySymsByLevel(candidate, layout, level);
                for (var i = 0; i < syms.Length; i++)
                {
                    if ((uint)syms[i] != keysym)
                    {
                        continue;
                    }

                    var masks = keymap.GetModsForLevel(candidate, layout, level);
                    if (masks.Length > 0)
                    {
                        keycode = candidate - 8;
                        modifiers = masks[0];
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets or sets the callback that commits text that the keymap cannot type, for example through an input method.
    /// </summary>
    public Func<string, bool>? CommitText { get; set; }

    /// <summary>
    /// Commits text through <see cref="CommitText"/>.
    /// </summary>
    /// <param name="text">The text to commit.</param>
    /// <returns><see langword="false"/> when no callback is set or the callback refuses the text.</returns>
    public bool TryCommitText(ReadOnlySpan<char> text) => CommitText is { } commit && commit(text.ToString());

    /// <inheritdoc/>
    public IDisposable? OverrideKeysyms(ReadOnlySpan<(uint Keycode, uint Keysym)> keys)
    {
        var device = _active;
        if (device.Compiled is not { } current || keys.Length == 0)
        {
            return null;
        }

        var saved = current.AsString(XkbKeymapFormat.TextV1);
        if (string.IsNullOrEmpty(saved) ||
            KeymapEdit.WithKeysyms(saved, keys) is not { } edited)
        {
            return null;
        }

        return Override(device, saved, edited);
    }

    private KeymapOverrideScope? Override(KeyboardDevice device, string saved, string text)
    {
        var savedModifiers = device.Modifiers;
        var before = device.File;
        if (!ApplyText(device, text) || ReferenceEquals(before, device.File))
        {
            return null;
        }

        return new KeymapOverrideScope(this, device, saved, savedModifiers);
    }

    private void Restore(KeyboardDevice device, string saved, (uint Depressed, uint Latched, uint Locked, uint Group) modifiers)
    {
        if (!IsLive(device) || !ApplyText(device, saved))
        {
            return;
        }

        if (ReferenceEquals(device, _active))
        {
            NotifyModifiers(modifiers.Depressed, modifiers.Latched, modifiers.Locked, modifiers.Group);
            return;
        }

        device.State?.UpdateMask(modifiers.Depressed, modifiers.Latched, modifiers.Locked, 0, 0, modifiers.Group);
        device.Modifiers = modifiers;
    }

    /// <summary>
    /// Replaces the keymap of the active keyboard device with a <c>pc+us</c> keymap in which one key produces a keysym. Clients receive the new keymap.
    /// </summary>
    /// <param name="keycode">The Linux evdev keycode to change.</param>
    /// <param name="keysym">The keysym that the key produces.</param>
    /// <returns>A scope that restores the previous keymap and modifiers when disposed, or <see langword="null"/> when nothing changed.</returns>
    public IDisposable? OverrideKeymapForKeysym(uint keycode, uint keysym)
    {
        var device = _active;
        if (device.Compiled is not { } current)
        {
            return null;
        }

        var name = new XkbKeysym(keysym).ToString();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var saved = current.AsString(XkbKeymapFormat.TextV1);
        if (string.IsNullOrEmpty(saved))
        {
            return null;
        }

        var custom =
            "xkb_keymap {\n" +
            "  xkb_keycodes \"custom\" {\n" +
            $"    <CSTM> = {keycode + 8};\n" +
            "  };\n" +
            "  xkb_types \"(custom)\" { include \"complete\" };\n" +
            "  xkb_compatibility \"custom\" { include \"complete\" };\n" +
            "  xkb_symbols \"custom\" {\n" +
            "    include \"pc+us\"\n" +
            $"    key <CSTM> {{ [ {name} ] }};\n" +
            "  };\n" +
            "};\n";
        return Override(device, saved, custom);
    }

    /// <summary>
    /// Pushes a grab and enters it on the current focus. It receives every notify call until it ends or a newer grab starts.
    /// </summary>
    /// <param name="grab">The grab.</param>
    public void StartGrab(IKeyboardGrab grab)
    {
        _grabs.Add(grab);
        grab.Enter(Focus, CollectPressed());
    }

    /// <summary>
    /// Removes a grab. When it was the active grab, its <see cref="IKeyboardGrab.Cancel"/> runs. The focus is not entered again.
    /// </summary>
    /// <param name="grab">The grab. A grab that is not started is ignored.</param>
    public void EndGrab(IKeyboardGrab grab)
    {
        var wasActive = HasGrab && Grab == grab;
        _grabs.Remove(grab);
        if (wasActive)
        {
            grab.Cancel();
        }
    }

    /// <summary>
    /// Sends leave to the old focus, then enter and modifiers to the new one, and raises <see cref="FocusChanged"/>. The same focus again does nothing. It bypasses grabs.
    /// </summary>
    /// <param name="surface">The new focus, or <see langword="null"/>.</param>
    /// <param name="pressedKeys">The evdev keycodes to list in <c>wl_keyboard.enter</c>.</param>
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

    /// <summary>
    /// Sends a key event to the focused client with a new serial. It bypasses grabs and does not update the xkb state.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The Linux evdev keycode.</param>
    /// <param name="state">The key state.</param>
    /// <returns>The serial, issued also when no client has focus.</returns>
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

    /// <summary>
    /// Sends the current <see cref="ModifierState"/> to the focused client. It bypasses grabs.
    /// </summary>
    public void SendModifiers()
    {
        if (_seat.ClientOf(Focus) is { } client)
        {
            SendModifiersTo(client);
        }
    }

    /// <inheritdoc/>
    public int TextFor(uint key, Span<char> into)
    {
        if (State is not { } state ||
            state.IsModActive(XkbNames.ModCtrl) || state.IsModActive(XkbNames.ModMod1) || state.IsModActive(XkbNames.ModMod4))
        {
            return 0;
        }

        if (Compose() is { } compose && compose.Feed(state.GetKeyOneSym(key + 8)) == XkbComposeFeedResult.Accepted)
        {
            switch (compose.Status)
            {
                case XkbComposeStatus.Composing:
                    return 0;
                case XkbComposeStatus.Cancelled:
                    compose.Reset();
                    return 0;
                case XkbComposeStatus.Composed:
                    var composed = compose.GetOneSym().Utf32;
                    if (composed != 0)
                    {
                        compose.Reset();
                        return Encode(composed, into);
                    }

                    var text = compose.GetUtf8();
                    compose.Reset();
                    if (text.Length > into.Length)
                    {
                        return 0;
                    }

                    text.AsSpan().CopyTo(into);
                    return text.Length;
            }
        }

        return Encode(state.GetKeyUtf32(key + 8), into);
    }

    /// <summary>
    /// Frees every device keymap and state, the default keymap source and the compose state. The seat calls it on dispose.
    /// </summary>
    public void Dispose()
    {
        foreach (var device in _devices)
        {
            device.Teardown();
        }

        _devices.Clear();
        _keymapSource.Dispose();
        _compose?.Dispose();
        _compose = null;
        _composeContext?.Dispose();
        _composeContext = null;
    }

    private XkbComposeState? Compose()
    {
        if (_composeLoaded)
        {
            return _compose;
        }

        _composeLoaded = true;
        var locale = Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } all ? all
            : Environment.GetEnvironmentVariable("LC_CTYPE") is { Length: > 0 } ctype ? ctype
            : Environment.GetEnvironmentVariable("LANG") is { Length: > 0 } lang ? lang
            : "C";
        try
        {
            _composeContext = XkbContext.Create();
            using var table = _composeContext.CreateComposeTable(locale);
            _compose = table.CreateState();
        }
        catch (XkbException error)
        {
            SeatLog.Log.Debug(error, $"keyboard: no compose table for {locale}");
            _compose = null;
        }

        return _compose;
    }

    private static int Encode(uint codepoint, Span<char> into)
    {
        if (codepoint is <= 0x1f or 0x7f || !Rune.TryCreate(codepoint, out var rune))
        {
            return 0;
        }

        return rune.TryEncodeToUtf16(into, out var written) ? written : 0;
    }

    internal void InitializeResource(WlKeyboardResource keyboard)
    {
        if (_broadcast is { } keymap)
        {
            keyboard.SendKeymap(WlKeyboard.KeymapFormat.XkbV1, keymap.FdFor(keyboard.Client), keymap.Size);
        }

        if (keyboard.Version >= 4)
        {
            keyboard.SendRepeatInfo(_sentRepeat.Rate, _sentRepeat.Delay);
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
        device.LedIndices = compiled is { } map
            ? (map.GetLedIndex(Xkb.XkbNames.LedNum),
               map.GetLedIndex(Xkb.XkbNames.LedCaps),
               map.GetLedIndex(Xkb.XkbNames.LedScroll),
               map.GetLedIndex(Xkb.XkbNames.LedCompose),
               map.GetLedIndex(Xkb.XkbNames.LedKana))
            : default;
        if (ReferenceEquals(device, _active))
        {
            var previousModifiers = _modifiers;
            BroadcastKeymap(file);
            _modifiers = device.Modifiers;
            UpdateLeds();
            if (_modifiers != previousModifiers)
            {
                Grab.Modifiers();
                AllocationScope.Pause();
                try
                {
                    ModifiersChanged?.Invoke();
                }
                finally
                {
                    AllocationScope.Resume();
                }
            }
        }
        else if (oldFile is not null && ReferenceEquals(_broadcast, oldFile))
        {
            _broadcast = _active.File;
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
        UpdateLeds();
        if (changed)
        {
            Grab.Modifiers();
            AllocationScope.Pause();
            try
            {
                ModifiersChanged?.Invoke();
            }
            finally
            {
                AllocationScope.Resume();
            }
        }
    }

    private void UpdateLeds()
    {
        var leds = KeyboardLeds.None;
        if (_active.State is { } state)
        {
            var indices = _active.LedIndices;
            if (indices.Num is { } num && state.IsLedActive(num))
            {
                leds |= KeyboardLeds.NumLock;
            }

            if (indices.Caps is { } caps && state.IsLedActive(caps))
            {
                leds |= KeyboardLeds.CapsLock;
            }

            if (indices.Scroll is { } scroll && state.IsLedActive(scroll))
            {
                leds |= KeyboardLeds.ScrollLock;
            }

            if (indices.Compose is { } compose && state.IsLedActive(compose))
            {
                leds |= KeyboardLeds.Compose;
            }

            if (indices.Kana is { } kana && state.IsLedActive(kana))
            {
                leds |= KeyboardLeds.Kana;
            }
        }

        if (leds != Leds)
        {
            Leds = leds;
            AllocationScope.Pause();
            try
            {
                LedsChanged?.Invoke();
            }
            finally
            {
                AllocationScope.Resume();
            }
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

    private uint[] CollectPressed()
    {
        if (_consumedCount == 0)
        {
            return _pressedKeys.ToArray();
        }

        var delivered = new List<uint>(_pressedKeys.Count);
        foreach (var key in _pressedKeys)
        {
            if (!WasConsumed(key))
            {
                delivered.Add(key);
            }
        }

        return [.. delivered];
    }

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

    private sealed class KeymapOverrideScope(
        SeatKeyboard keyboard,
        KeyboardDevice device,
        string saved,
        (uint Depressed, uint Latched, uint Locked, uint Group) modifiers) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            keyboard.Restore(device, saved, modifiers);
        }
    }

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
