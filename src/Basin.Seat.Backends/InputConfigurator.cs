using Basin.Backend.Libinput;
using Basin.Capabilities;

namespace Basin.Seat.Backends;

/// <summary>
/// Applies input rules to devices at start, on hotplug and on reload, over a backend configuration. Runtime overrides win over rules, and rules win over device defaults.
/// </summary>
public sealed class InputConfigurator : IInputDeviceConfiguration, IInputSettingSources, IDisposable
{
    private static readonly int SettingCount = InputSettingNames.Settings.Length;

    private static readonly InputSetting[] KeymapKeys =
    [
        InputSetting.KeymapRules,
        InputSetting.KeymapModel,
        InputSetting.KeymapLayout,
        InputSetting.KeymapVariant,
        InputSetting.KeymapOptions,
    ];

    private readonly Dictionary<ulong, DeviceState> _devices = [];
    private readonly Dictionary<InputDevice, ulong> _ownIds = [];
    private readonly Dictionary<InputDevice, KeyboardDevice> _keyboards = [];
    private readonly Dictionary<InputDevice, double> _scrollFactors = [];
    private readonly Dictionary<InputDevice, string> _outputNames = [];
    private readonly HashSet<(int Rule, ulong Device, InputSetting Setting)> _reported = [];
    private readonly string?[] _fallbackScratch = new string?[KeymapKeys.Length];
    private IReadOnlyList<InputRule> _rules = [];
    private int[] _order = [];
    private Seat? _seat;
    private SeatBinder? _binder;
    private IInputDeviceConfiguration? _backend;
    private LibinputDeviceConfiguration? _libinput;
    private bool _attached;
    private bool _ownsKeyboardFor;
    private bool _disposed;
    private ulong _nextOwnId = 1UL << 48;
    private (int Rate, int Delay) _baselineRepeat;
    private KeymapNames _fallbackNames;
    private (int Rate, int Delay) _fallbackRepeat;
    private bool _fallbackKeymapSet;
    private bool _baselineAdopted;

    /// <inheritdoc/>
    public event Action<InputDeviceInfo>? DeviceAdded;

    /// <inheritdoc/>
    public event Action<ulong>? DeviceRemoved;

    /// <inheritdoc/>
    public event Action<ulong, InputSetting>? Changed;

    /// <summary>
    /// Occurs once per rule, device and setting when a rule names a setting that the device does not support or a value that the backend refuses.
    /// </summary>
    public event Action<InputRuleOutcome>? Rejected;

    /// <summary>
    /// Gets a value indicating whether <see cref="Attach"/> has run.
    /// </summary>
    public bool IsAttached => _attached;

    /// <summary>
    /// Gets the rules from the last <see cref="Apply"/>, in their given order.
    /// </summary>
    public IReadOnlyList<InputRule> Rules => _rules;

    /// <summary>
    /// Gets a value indicating whether a rule that names no device sets a keymap field. The binder then keeps the parent's keymap off the seat.
    /// </summary>
    public bool NamesSeatKeymap { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a rule that names no device sets the repeat rate or delay.
    /// </summary>
    public bool NamesSeatRepeat { get; private set; }

    /// <summary>
    /// Connects the configurator to the seat, the binder and the backend, and applies the rules to every known device. It takes the binder's scroll factor and output name callbacks.
    /// </summary>
    /// <param name="seat">The seat whose keyboard gets the keymap and repeat settings.</param>
    /// <param name="binder">The binder that reports devices and reads the per-device callbacks.</param>
    /// <param name="backend">The libinput configuration, or <see langword="null"/> for a nested or headless run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> or <paramref name="binder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The configurator is disposed.</exception>
    /// <exception cref="InvalidOperationException">The configurator is attached already.</exception>
    /// <exception cref="ArgumentException"><paramref name="backend"/> is this configurator.</exception>
    public void Attach(Seat seat, SeatBinder binder, IInputDeviceConfiguration? backend)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(binder);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_attached)
        {
            throw new InvalidOperationException("the input configurator is already attached");
        }

        if (ReferenceEquals(backend, this))
        {
            throw new ArgumentException("the configurator cannot sit on itself", nameof(backend));
        }

        _attached = true;
        _seat = seat;
        _binder = binder;
        _backend = backend;
        _libinput = backend as LibinputDeviceConfiguration;
        if (!_baselineAdopted)
        {
            _baselineRepeat = seat.Keyboard.SeatRepeatInfo;
        }

        _fallbackRepeat = seat.Keyboard.SeatRepeatInfo;

        if (binder.KeyboardFor is null)
        {
            binder.KeyboardFor = KeyboardForDevice;
            _ownsKeyboardFor = true;
        }

        binder.ScrollFactorFor = ScrollFactorOf;
        binder.OutputNameFor = OutputNameOf;
        binder.Configurator = this;
        binder.DeviceAdded += OnBinderDeviceAdded;
        binder.DeviceRemoved += OnBinderDeviceRemoved;

        if (backend is not null)
        {
            backend.DeviceAdded += OnBackendDeviceAdded;
            backend.DeviceRemoved += OnBackendDeviceRemoved;
            backend.Changed += OnBackendChanged;
        }

        ReportDetachedRules();
        ResolveFallback();
        if (backend is not null)
        {
            var devices = new InputDeviceInfo[16];
            var count = backend.Enumerate(devices);
            while (count < 0)
            {
                devices = new InputDeviceInfo[devices.Length * 2];
                count = backend.Enumerate(devices);
            }

            for (var i = 0; i < count; i++)
            {
                OnBackendDeviceAdded(devices[i]);
            }
        }
    }

    /// <summary>
    /// Replaces the rules and applies them to every device. Before <see cref="Attach"/>, it only stores them. A key that no rule names any more returns to its default, and runtime overrides stay.
    /// </summary>
    /// <param name="rules">The new rules, in file order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The configurator is disposed.</exception>
    public void Apply(IReadOnlyList<InputRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _rules = rules;
        _order = OrderOf(rules);
        _reported.Clear();
        if (!_attached)
        {
            return;
        }

        ReportDetachedRules();
        ResolveFallback();
        foreach (var state in _devices.Values)
        {
            MatchRules(state);
            Resolve(state);
        }
    }

    /// <summary>
    /// Takes a repeat rate and delay as the baseline under the rules, for example the parent's repeat in a nested run.
    /// </summary>
    /// <param name="rate">The repeat rate in keys per second.</param>
    /// <param name="delay">The repeat delay in milliseconds.</param>
    public void AdoptRepeatInfo(int rate, int delay)
    {
        _baselineRepeat = (rate, delay);
        _baselineAdopted = true;
        if (!_attached)
        {
            return;
        }

        ResolveFallback();
        foreach (var state in _devices.Values)
        {
            ResolveKeyboard(state);
        }
    }

    /// <inheritdoc/>
    public InputSettingLayer SourceOf(ulong deviceId, InputSetting setting)
    {
        if (!_devices.TryGetValue(deviceId, out var state))
        {
            return InputSettingLayer.Default;
        }

        if (state.Runtime.ContainsKey(setting))
        {
            return InputSettingLayer.Runtime;
        }

        return state.HasConfig[(int)setting] ? InputSettingLayer.Config : InputSettingLayer.Default;
    }

    /// <inheritdoc/>
    public IReadOnlyList<int> RulesFor(ulong deviceId) =>
        _devices.TryGetValue(deviceId, out var state) ? state.Matched : [];

    /// <inheritdoc/>
    public int Enumerate(Span<InputDeviceInfo> devices)
    {
        if (_devices.Count > devices.Length)
        {
            return -1;
        }

        var written = 0;
        foreach (var state in _devices.Values)
        {
            devices[written++] = state.Info;
        }

        return written;
    }

    /// <inheritdoc/>
    public InputSettingSupport Support(ulong deviceId, InputSetting setting)
    {
        if (!_devices.TryGetValue(deviceId, out var state))
        {
            return InputSettingSupport.None;
        }

        if (InputSettingNames.IsLibinput(setting))
        {
            return state.BackendOwned && _backend is { } backend ? backend.Support(deviceId, setting) : InputSettingSupport.None;
        }

        return SupportsLocal(state, setting) ? InputSettingSupport.Yes : InputSettingSupport.None;
    }

    /// <inheritdoc/>
    public bool TryGet(ulong deviceId, InputSetting setting, out InputSettingValue value)
    {
        value = default;
        if (!_devices.TryGetValue(deviceId, out var state))
        {
            return false;
        }

        if (InputSettingNames.IsLibinput(setting))
        {
            return state.BackendOwned && _backend is { } backend && backend.TryGet(deviceId, setting, out value);
        }

        if (!SupportsLocal(state, setting))
        {
            return false;
        }

        value = LocalValue(state, setting);
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetDefault(ulong deviceId, InputSetting setting, out InputSettingValue value)
    {
        value = default;
        if (!_devices.TryGetValue(deviceId, out var state))
        {
            return false;
        }

        if (InputSettingNames.IsLibinput(setting))
        {
            return state.BackendOwned && _backend is { } backend && backend.TryGetDefault(deviceId, setting, out value);
        }

        if (!SupportsLocal(state, setting))
        {
            return false;
        }

        value = LocalDefault(setting);
        return true;
    }

    /// <summary>
    /// Sets a runtime override for a device setting. The override survives a reload and ends with the device.
    /// </summary>
    /// <param name="deviceId">The device identifier.</param>
    /// <param name="setting">The setting to change.</param>
    /// <param name="value">The new value.</param>
    /// <returns><see cref="InputSettingResult.Unsupported"/> before <see cref="Attach"/> or for a setting the device lacks, and <see cref="InputSettingResult.Invalid"/> for an unknown device or a refused value.</returns>
    public InputSettingResult Set(ulong deviceId, InputSetting setting, in InputSettingValue value)
    {
        if (!_attached)
        {
            return InputSettingResult.Unsupported;
        }

        if (!_devices.TryGetValue(deviceId, out var state))
        {
            return InputSettingResult.Invalid;
        }

        if (InputSettingNames.IsLibinput(setting))
        {
            if (!state.BackendOwned || _backend is not { } backend)
            {
                return InputSettingResult.Unsupported;
            }

            var outcome = backend.Set(deviceId, setting, value);
            if (outcome == InputSettingResult.Success)
            {
                state.Runtime[setting] = value;
            }

            return outcome;
        }

        if (!SupportsLocal(state, setting))
        {
            return InputSettingResult.Unsupported;
        }

        if (!IsValidLocal(setting, value))
        {
            return InputSettingResult.Invalid;
        }

        state.Runtime[setting] = value;
        ResolveLocal(state);
        return InputSettingResult.Success;
    }

    /// <summary>
    /// Removes the runtime override of a device setting, so that the rule value or the default comes back.
    /// </summary>
    /// <param name="deviceId">The device identifier.</param>
    /// <param name="setting">The setting to reset.</param>
    /// <returns><see cref="InputSettingResult.Unsupported"/> before <see cref="Attach"/> or for a setting the device lacks, and <see cref="InputSettingResult.Invalid"/> for an unknown device.</returns>
    public InputSettingResult Reset(ulong deviceId, InputSetting setting)
    {
        if (!_attached)
        {
            return InputSettingResult.Unsupported;
        }

        if (!_devices.TryGetValue(deviceId, out var state))
        {
            return InputSettingResult.Invalid;
        }

        if (!Support(deviceId, setting).Supported)
        {
            return InputSettingResult.Unsupported;
        }

        state.Runtime.Remove(setting);
        if (InputSettingNames.IsLibinput(setting))
        {
            return ResolveLibinput(state, setting, report: false);
        }

        ResolveLocal(state);
        return InputSettingResult.Success;
    }

    /// <summary>
    /// Sets a custom acceleration curve as a runtime override and records the custom profile.
    /// </summary>
    /// <param name="deviceId">The device identifier.</param>
    /// <param name="type">The acceleration type that the curve applies to.</param>
    /// <param name="step">The distance between two curve points.</param>
    /// <param name="points">The curve points.</param>
    /// <returns><see cref="InputSettingResult.Unsupported"/> before <see cref="Attach"/> or without a backend, and <see cref="InputSettingResult.Invalid"/> for an unknown device.</returns>
    public InputSettingResult SetAccelCurve(ulong deviceId, InputAccelCurveType type, double step, ReadOnlySpan<double> points)
    {
        if (!_attached)
        {
            return InputSettingResult.Unsupported;
        }

        if (!_devices.TryGetValue(deviceId, out var state))
        {
            return InputSettingResult.Invalid;
        }

        if (!state.BackendOwned || _backend is not { } backend)
        {
            return InputSettingResult.Unsupported;
        }

        var outcome = backend.SetAccelCurve(deviceId, type, step, points);
        if (outcome == InputSettingResult.Success)
        {
            state.Runtime[InputSetting.AccelProfile] = InputSettingValue.Of(InputAccelProfile.Custom);
        }

        return outcome;
    }

    internal KeyboardDevice? KeyboardDeviceFor(ulong deviceId) =>
        _devices.TryGetValue(deviceId, out var state) ? state.Keyboard : null;

    internal double ScrollFactorFor(ulong deviceId) =>
        _devices.TryGetValue(deviceId, out var state) ? state.ScrollFactor : 1.0;

    /// <summary>
    /// Detaches from the binder and the backend, gives back the binder callbacks, and disposes the keyboard devices that the configurator made.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_backend is { } backend)
        {
            backend.DeviceAdded -= OnBackendDeviceAdded;
            backend.DeviceRemoved -= OnBackendDeviceRemoved;
            backend.Changed -= OnBackendChanged;
        }

        if (_binder is { } binder)
        {
            binder.DeviceAdded -= OnBinderDeviceAdded;
            binder.DeviceRemoved -= OnBinderDeviceRemoved;
            binder.ScrollFactorFor = null;
            binder.OutputNameFor = null;
            binder.Configurator = null;
            if (_ownsKeyboardFor)
            {
                binder.KeyboardFor = null;
            }
        }

        foreach (var state in _devices.Values)
        {
            if (state.OwnsKeyboard)
            {
                state.Keyboard?.Dispose();
            }
        }

        _devices.Clear();
        _keyboards.Clear();
        _scrollFactors.Clear();
        _outputNames.Clear();
        _ownIds.Clear();
    }

    private Capabilities.IInjectedKeyboard? KeyboardForDevice(InputDevice device) => _keyboards.GetValueOrDefault(device);

    private double ScrollFactorOf(InputDevice device) => _scrollFactors.TryGetValue(device, out var factor) ? factor : 1.0;

    private string? OutputNameOf(InputDevice device) => _outputNames.GetValueOrDefault(device);

    private void OnBackendDeviceAdded(InputDeviceInfo info)
    {
        if (_devices.ContainsKey(info.Id))
        {
            return;
        }

        var state = new DeviceState(info, backendOwned: true)
        {
            Device = _libinput?.DeviceFor(info.Id),
        };
        Add(state);
    }

    private void OnBackendDeviceRemoved(ulong id) => Remove(id);

    private void OnBackendChanged(ulong id, InputSetting setting) => Changed?.Invoke(id, setting);

    private void OnBinderDeviceAdded(InputDevice device)
    {
        if (_libinput is { } libinput)
        {
            var id = libinput.IdFor(device);
            if (id != 0 && _devices.TryGetValue(id, out var owned))
            {
                owned.Device ??= device;
                ResolveLocal(owned);
            }

            return;
        }

        if (_ownIds.ContainsKey(device))
        {
            return;
        }

        var own = _nextOwnId++;
        _ownIds[device] = own;
        Add(new DeviceState(LibinputDeviceConfiguration.Describe(own, device), backendOwned: false) { Device = device });
    }

    private void OnBinderDeviceRemoved(InputDevice device)
    {
        if (_ownIds.Remove(device, out var id))
        {
            Remove(id);
        }
    }

    private void Add(DeviceState state)
    {
        _devices[state.Info.Id] = state;
        MatchRules(state);
        Resolve(state);
        state.Resolved = true;
        DeviceAdded?.Invoke(state.Info);
    }

    private void Remove(ulong id)
    {
        if (!_devices.Remove(id, out var state))
        {
            return;
        }

        if (state.Device is { } device)
        {
            _keyboards.Remove(device);
            _scrollFactors.Remove(device);
            _outputNames.Remove(device);
        }

        if (state.OwnsKeyboard)
        {
            state.Keyboard?.Dispose();
        }

        _reported.RemoveWhere(key => key.Device == id);

        DeviceRemoved?.Invoke(id);
    }

    private void MatchRules(DeviceState state)
    {
        Array.Clear(state.HasConfig);
        state.Matched.Clear();
        foreach (var index in _order)
        {
            var rule = _rules[index];
            if (!rule.Match.Matches(state.Info))
            {
                continue;
            }

            state.Matched.Add(index);
            for (var i = 0; i < rule.Settings.Count; i++)
            {
                var (setting, value) = rule.Settings[i];
                var slot = (int)setting;
                if (!state.HasConfig[slot])
                {
                    state.HasConfig[slot] = true;
                    state.Config[slot] = value;
                    state.ConfigRule[slot] = index;
                }
            }
        }

        state.Matched.Sort();
    }

    private void Resolve(DeviceState state)
    {
        foreach (var setting in InputSettingNames.Settings)
        {
            if (InputSettingNames.IsLibinput(setting))
            {
                ResolveLibinput(state, setting, report: true);
            }
        }

        ResolveLocal(state);
    }

    private InputSettingResult ResolveLibinput(DeviceState state, InputSetting setting, bool report)
    {
        var slot = (int)setting;
        var id = state.Info.Id;
        if (!state.BackendOwned || _backend is not { } backend || !backend.Support(id, setting).Supported)
        {
            if (report && state.HasConfig[slot])
            {
                Report(state.ConfigRule[slot], state, setting, InputSettingResult.Unsupported);
            }

            return InputSettingResult.Unsupported;
        }

        var rule = -1;
        if (!state.Runtime.TryGetValue(setting, out var target))
        {
            if (state.HasConfig[slot])
            {
                target = state.Config[slot];
                rule = state.ConfigRule[slot];
            }
            else if (!backend.TryGetDefault(id, setting, out target))
            {
                return InputSettingResult.Unsupported;
            }
        }

        if (backend.TryGet(id, setting, out var current) && current.Matches(target))
        {
            return InputSettingResult.Success;
        }

        var outcome = backend.Set(id, setting, target);
        if (outcome != InputSettingResult.Success && report && rule >= 0)
        {
            Report(rule, state, setting, outcome);
        }

        return outcome;
    }

    private void ResolveLocal(DeviceState state)
    {
        ResolveKeyboard(state);
        ResolveCompositor(state);
    }

    private void ResolveCompositor(DeviceState state)
    {
        if (SupportsLocal(state, InputSetting.ScrollFactor))
        {
            var factor = Effective(state, InputSetting.ScrollFactor, out var value) ? value[0] : 1.0;
            if (factor != state.ScrollFactor)
            {
                state.ScrollFactor = factor;
                if (state.Device is { } device)
                {
                    if (factor == 1.0)
                    {
                        _scrollFactors.Remove(device);
                    }
                    else
                    {
                        _scrollFactors[device] = factor;
                    }
                }

                RaiseLocal(state, InputSetting.ScrollFactor);
            }
        }
        else
        {
            ReportLocal(state, InputSetting.ScrollFactor);
        }

        if (SupportsLocal(state, InputSetting.MapToOutput))
        {
            var output = Effective(state, InputSetting.MapToOutput, out var value) ? value.TextValue ?? string.Empty : string.Empty;
            if (!string.Equals(output, state.OutputName, StringComparison.Ordinal))
            {
                state.OutputName = output;
                if (state.Device is { } device)
                {
                    if (output.Length == 0)
                    {
                        _outputNames.Remove(device);
                    }
                    else
                    {
                        _outputNames[device] = output;
                    }
                }

                RaiseLocal(state, InputSetting.MapToOutput);
            }
        }
        else
        {
            ReportLocal(state, InputSetting.MapToOutput);
        }
    }

    private void ResolveKeyboard(DeviceState state)
    {
        if (!SupportsLocal(state, InputSetting.KeymapLayout))
        {
            foreach (var key in KeymapKeys)
            {
                ReportLocal(state, key);
            }

            ReportLocal(state, InputSetting.RepeatRate);
            ReportLocal(state, InputSetting.RepeatDelay);
            return;
        }

        var names = new KeymapNames(
            KeymapText(state, InputSetting.KeymapRules, _fallbackNames.Rules),
            KeymapText(state, InputSetting.KeymapModel, _fallbackNames.Model),
            KeymapText(state, InputSetting.KeymapLayout, _fallbackNames.Layout),
            KeymapText(state, InputSetting.KeymapVariant, _fallbackNames.Variant),
            KeymapText(state, InputSetting.KeymapOptions, _fallbackNames.Options));
        var repeat = (
            Effective(state, InputSetting.RepeatRate, out var rate) ? (int)rate.Value : _fallbackRepeat.Rate,
            Effective(state, InputSetting.RepeatDelay, out var delay) ? (int)delay.Value : _fallbackRepeat.Delay);

        var ownKeymap = names != _fallbackNames;
        var ownRepeat = repeat != _fallbackRepeat;
        if ((ownKeymap || ownRepeat) && state.Keyboard is null)
        {
            AcquireKeyboard(state);
        }

        if (state.Keyboard is { } keyboard)
        {
            if (ownKeymap && (!state.KeymapSet || names != state.Names))
            {
                if (keyboard.SetKeymap(names))
                {
                    state.KeymapSet = true;
                }
                else
                {
                    ReportKeymap(state);
                }
            }
            else if (!ownKeymap && state.KeymapSet)
            {
                keyboard.ClearKeymap();
                state.KeymapSet = false;
            }

            if (ownRepeat)
            {
                if (!state.RepeatSet || repeat != state.Repeat)
                {
                    keyboard.SetRepeatInfo(repeat.Item1, repeat.Item2);
                    state.RepeatSet = true;
                }
            }
            else if (state.RepeatSet)
            {
                keyboard.ClearRepeatInfo();
                state.RepeatSet = false;
            }
        }

        var previousNames = state.Names;
        var previousRepeat = state.Repeat;
        state.Names = names;
        state.Repeat = repeat;
        if (!state.Resolved)
        {
            return;
        }

        if (previousNames.Rules != names.Rules)
        {
            RaiseLocal(state, InputSetting.KeymapRules);
        }

        if (previousNames.Model != names.Model)
        {
            RaiseLocal(state, InputSetting.KeymapModel);
        }

        if (previousNames.Layout != names.Layout)
        {
            RaiseLocal(state, InputSetting.KeymapLayout);
        }

        if (previousNames.Variant != names.Variant)
        {
            RaiseLocal(state, InputSetting.KeymapVariant);
        }

        if (previousNames.Options != names.Options)
        {
            RaiseLocal(state, InputSetting.KeymapOptions);
        }

        if (previousRepeat.Rate != repeat.Item1)
        {
            RaiseLocal(state, InputSetting.RepeatRate);
        }

        if (previousRepeat.Delay != repeat.Item2)
        {
            RaiseLocal(state, InputSetting.RepeatDelay);
        }
    }

    private void AcquireKeyboard(DeviceState state)
    {
        if (_ownsKeyboardFor)
        {
            state.Keyboard = _seat!.Keyboard.CreateDevice();
            state.OwnsKeyboard = true;
            if (state.Device is { } device)
            {
                _keyboards[device] = state.Keyboard;
            }

            return;
        }

        if (state.Device is { } consumer && _binder!.KeyboardFor?.Invoke(consumer) is KeyboardDevice given)
        {
            state.Keyboard = given;
        }
    }

    private void ResolveFallback()
    {
        var keymap = _fallbackScratch;
        Array.Clear(keymap);
        var namedKeymap = false;
        int? rate = null;
        int? delay = null;
        foreach (var index in _order)
        {
            var rule = _rules[index];
            if (!rule.Match.MatchesKeyboardFallback())
            {
                continue;
            }

            for (var k = 0; k < KeymapKeys.Length; k++)
            {
                if (keymap[k] is null && rule.Names(KeymapKeys[k], out var text))
                {
                    keymap[k] = text.TextValue;
                    namedKeymap = true;
                }
            }

            if (rate is null && rule.Names(InputSetting.RepeatRate, out var r))
            {
                rate = (int)r.Value;
            }

            if (delay is null && rule.Names(InputSetting.RepeatDelay, out var d))
            {
                delay = (int)d.Value;
            }
        }

        var names = new KeymapNames(Blank(keymap[0]), Blank(keymap[1]), Blank(keymap[2]), Blank(keymap[3]), Blank(keymap[4]));
        var seat = _seat!.Keyboard;
        NamesSeatKeymap = namedKeymap;
        if (namedKeymap)
        {
            if (!_fallbackKeymapSet || names != _fallbackNames)
            {
                seat.SetKeymap(names);
                _fallbackKeymapSet = true;
            }
        }
        else if (_fallbackKeymapSet)
        {
            seat.SetKeymap(SystemKeymap.Read());
            _fallbackKeymapSet = false;
        }

        _fallbackNames = names;

        var repeat = (rate ?? _baselineRepeat.Rate, delay ?? _baselineRepeat.Delay);
        NamesSeatRepeat = rate is not null || delay is not null;
        if (repeat != seat.SeatRepeatInfo)
        {
            seat.SetRepeatInfo(repeat.Item1, repeat.Item2);
        }

        _fallbackRepeat = repeat;
    }

    private void ReportDetachedRules()
    {
        if (_backend is not null)
        {
            return;
        }

        for (var index = 0; index < _rules.Count; index++)
        {
            var rule = _rules[index];
            for (var i = 0; i < rule.Settings.Count; i++)
            {
                var setting = rule.Settings[i].Setting;
                if (InputSettingNames.IsLibinput(setting) && _reported.Add((index, 0, setting)))
                {
                    Rejected?.Invoke(new InputRuleOutcome(index, rule.Line, 0, null, setting, InputSettingResult.Unsupported));
                }
            }
        }
    }

    private void Report(int rule, DeviceState state, InputSetting setting, InputSettingResult result)
    {
        if (_reported.Add((rule, state.Info.Id, setting)))
        {
            Rejected?.Invoke(new InputRuleOutcome(rule, _rules[rule].Line, state.Info.Id, state.Info.Name, setting, result));
        }
    }

    private void ReportLocal(DeviceState state, InputSetting setting)
    {
        var slot = (int)setting;
        if (state.HasConfig[slot])
        {
            Report(state.ConfigRule[slot], state, setting, InputSettingResult.Unsupported);
        }
    }

    private void ReportKeymap(DeviceState state)
    {
        foreach (var key in KeymapKeys)
        {
            var slot = (int)key;
            if (state.HasConfig[slot])
            {
                Report(state.ConfigRule[slot], state, key, InputSettingResult.Invalid);
                return;
            }
        }
    }

    private void RaiseLocal(DeviceState state, InputSetting setting)
    {
        if (state.Resolved)
        {
            Changed?.Invoke(state.Info.Id, setting);
        }
    }

    private static bool Effective(DeviceState state, InputSetting setting, out InputSettingValue value)
    {
        if (state.Runtime.TryGetValue(setting, out value))
        {
            return true;
        }

        var slot = (int)setting;
        if (state.HasConfig[slot])
        {
            value = state.Config[slot];
            return true;
        }

        value = default;
        return false;
    }

    private static string? KeymapText(DeviceState state, InputSetting setting, string? fallback) =>
        Effective(state, setting, out var value) ? Blank(value.TextValue) : fallback;

    private static string? Blank(string? text) => string.IsNullOrEmpty(text) ? null : text;

    private InputSettingValue LocalValue(DeviceState state, InputSetting setting) => setting switch
    {
        InputSetting.KeymapRules => InputSettingValue.Text(state.Names.Rules ?? string.Empty),
        InputSetting.KeymapModel => InputSettingValue.Text(state.Names.Model ?? string.Empty),
        InputSetting.KeymapLayout => InputSettingValue.Text(state.Names.Layout ?? string.Empty),
        InputSetting.KeymapVariant => InputSettingValue.Text(state.Names.Variant ?? string.Empty),
        InputSetting.KeymapOptions => InputSettingValue.Text(state.Names.Options ?? string.Empty),
        InputSetting.RepeatRate => InputSettingValue.Of((uint)state.Repeat.Rate),
        InputSetting.RepeatDelay => InputSettingValue.Of((uint)state.Repeat.Delay),
        InputSetting.ScrollFactor => InputSettingValue.Number(state.ScrollFactor),
        InputSetting.MapToOutput => InputSettingValue.Text(state.OutputName),
        _ => default,
    };

    private InputSettingValue LocalDefault(InputSetting setting) => setting switch
    {
        InputSetting.RepeatRate => InputSettingValue.Of((uint)_baselineRepeat.Rate),
        InputSetting.RepeatDelay => InputSettingValue.Of((uint)_baselineRepeat.Delay),
        InputSetting.ScrollFactor => InputSettingValue.Number(1.0),
        _ => InputSettingValue.Text(string.Empty),
    };

    private static bool SupportsLocal(DeviceState state, InputSetting setting)
    {
        var capabilities = state.Info.Capabilities;
        if (InputSettingNames.IsKeyboard(setting))
        {
            return (capabilities & InputDeviceCapability.Keyboard) != 0;
        }

        return setting switch
        {
            InputSetting.ScrollFactor => (capabilities & InputDeviceCapability.Pointer) != 0,
            InputSetting.MapToOutput => (capabilities & (InputDeviceCapability.Pointer | InputDeviceCapability.Touch | InputDeviceCapability.TabletTool)) != 0,
            _ => false,
        };
    }

    private static bool IsValidLocal(InputSetting setting, in InputSettingValue value) => setting switch
    {
        InputSetting.ScrollFactor => value.Count == 1 && value[0] > 0 && double.IsFinite(value[0]),
        InputSetting.RepeatRate or InputSetting.RepeatDelay => value.Value <= int.MaxValue,
        _ => value.TextValue is not null,
    };

    private static int[] OrderOf(IReadOnlyList<InputRule> rules)
    {
        var order = new int[rules.Count];
        for (var i = 0; i < order.Length; i++)
        {
            var index = i;
            var specificity = rules[i].Match.Specificity;
            var at = i;
            while (at > 0)
            {
                var other = rules[order[at - 1]].Match.Specificity;
                if (other > specificity || (other == specificity && order[at - 1] > index))
                {
                    break;
                }

                order[at] = order[at - 1];
                at--;
            }

            order[at] = index;
        }

        return order;
    }

    private sealed class DeviceState(InputDeviceInfo info, bool backendOwned)
    {
        public InputDeviceInfo Info { get; } = info;

        public bool BackendOwned { get; } = backendOwned;

        public InputDevice? Device { get; set; }

        public Dictionary<InputSetting, InputSettingValue> Runtime { get; } = [];

        public InputSettingValue[] Config { get; } = new InputSettingValue[SettingCount];

        public bool[] HasConfig { get; } = new bool[SettingCount];

        public int[] ConfigRule { get; } = new int[SettingCount];

        public List<int> Matched { get; } = [];

        public bool Resolved { get; set; }

        public KeyboardDevice? Keyboard { get; set; }

        public bool OwnsKeyboard { get; set; }

        public bool KeymapSet { get; set; }

        public bool RepeatSet { get; set; }

        public KeymapNames Names { get; set; }

        public (int Rate, int Delay) Repeat { get; set; }

        public double ScrollFactor { get; set; } = 1.0;

        public string OutputName { get; set; } = string.Empty;
    }
}
