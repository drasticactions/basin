using System.Collections.ObjectModel;
using System.Globalization;
using Basin;
using Basin.Audio.PipeWire;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Ipc;
using Basin.SystemStatus;

namespace LunaWm;

internal sealed class LunaStatus : IDisposable
{
    private readonly ICompositorEventLoop _loop;
    private readonly IpcLineReport _report;
    private readonly BasinLogger _log;
    private readonly string _clientName;
    private SystemStatusBus? _statusBus;
    private BatteryStatus? _battery;
    private NetworkStatus? _network;
    private BluetoothStatus? _bluetooth;
    private AudioSession? _audio;
    private SinkVolume? _volume;
    private IOutputBrightness? _brightness;
    private NetworkAccessPoint? _wifiAsking;
    private string? _wifiMessage;

    public LunaStatus(ICompositorEventLoop loop, IpcLineReport report, BasinLogger log, string clientName)
    {
        _loop = loop;
        _report = report;
        _log = log;
        _clientName = clientName;
    }

    public NetworkStatus? Network => _network;

    public bool WifiAsking => _wifiAsking is not null;

    public bool JoinAllowed { get; set; } = true;

    public event Action? Changed;

    public event Action? WifiMenusChanged;

    public void Start(IOutputBrightness? brightness, bool battery = true, bool network = true, bool bluetooth = true, bool sound = true)
    {
        _brightness = brightness;
        _statusBus = new SystemStatusBus(_loop);
        string? batteryReason = null;
        string? networkReason = null;
        string? bluetoothReason = null;
        _battery = battery ? BatteryStatus.TryCreate(_statusBus, out batteryReason) : null;
        _network = network ? NetworkStatus.TryCreate(_statusBus, out networkReason) : null;
        _bluetooth = bluetooth ? BluetoothStatus.TryCreate(_statusBus, out bluetoothReason) : null;
        foreach (var reason in new[] { batteryReason, networkReason, bluetoothReason })
        {
            if (reason is not null)
            {
                _log.Info($"status: {reason}");
            }
        }

        if (_battery is not null)
        {
            _battery.Changed += OnChanged;
        }

        if (_network is not null)
        {
            _network.Changed += OnChanged;
        }

        if (_bluetooth is not null)
        {
            _bluetooth.Changed += OnChanged;
        }

        if (sound)
        {
            _audio = AudioSession.TryCreate(_loop, out var audioReason, clientName: _clientName);
            if (_audio is null)
            {
                _log.Info($"status: no volume indicator, {audioReason}");
            }
            else
            {
                _volume = new SinkVolume(_audio);
                _volume.Changed += _ => OnChanged();
            }
        }

        if (_brightness is not null)
        {
            _brightness.BrightnessChanged += _ => OnChanged();
        }

        OnChanged();
    }

    public void Dispose()
    {
        _volume?.Dispose();
        _volume = null;
        _audio?.Dispose();
        _audio = null;
        _battery?.Dispose();
        _network?.Dispose();
        _bluetooth?.Dispose();
        _statusBus?.Dispose();
        _battery = null;
        _network = null;
        _bluetooth = null;
        _statusBus = null;
    }

    private void OnChanged() => Changed?.Invoke();

    public void Sync(StatusBarModel model)
    {
        var battery = _battery is { IsAvailable: true, IsPresent: true } ? _battery : null;
        model.BatteryShown = battery is not null;
        if (battery is not null)
        {
            model.BatteryLevel = (int)Math.Round(Math.Clamp(battery.Percentage, 0, 100) * 11 / 100);
            model.BatteryCharging = battery.State is BatteryState.Charging or BatteryState.PendingCharge;
            model.BatteryError = battery.State == BatteryState.Unknown;
        }

        var wifi = _network is { IsAvailable: true, HasWirelessDevice: true } ? _network : null;
        model.WifiShown = wifi is not null;
        if (wifi is not null)
        {
            var strength = wifi.PrimaryKind == NetworkConnectionKind.Wireless ? wifi.PrimaryStrength ?? 0 : 0;
            model.WifiBars = !wifi.WirelessEnabled || wifi.PrimaryKind != NetworkConnectionKind.Wireless ? 0 : strength >= 67 ? 3 : strength >= 34 ? 2 : 1;
        }

        var bluetooth = _bluetooth is { IsAvailable: true, HasAdapter: true, Powered: true } ? _bluetooth : null;
        model.BluetoothShown = bluetooth is not null;
        model.BluetoothConnected = bluetooth?.AnyConnected == true;

        model.VolumeShown = _volume is { IsAvailable: true };
        if (_volume is { IsAvailable: true } volume)
        {
            model.Muted = volume.Muted;
            model.Volume = volume.Volume;
        }
    }

    public string IndicatorState
    {
        get
        {
            var battery = _battery is null ? "absent" : _battery.IsPresent ? $"{_battery.Percentage:0}%{(_battery.State == BatteryState.Charging ? "+" : "")}" : "none";
            var wifi = _network is { IsAvailable: true, HasWirelessDevice: true } network ? (network.WirelessEnabled ? $"on:{network.PrimaryStrength?.ToString(CultureInfo.InvariantCulture) ?? "-"}" : "off") : "hidden";
            var bluetooth = _bluetooth is { IsAvailable: true, HasAdapter: true } adapter ? (adapter.Powered ? (adapter.AnyConnected ? "connected" : "on") : "off") : "hidden";
            var volume = _volume is { IsAvailable: true } sink ? $"{sink.Volume:0.##}{(sink.Muted ? ":muted" : "")}" : "hidden";
            return $"battery={battery} wifi={wifi} bluetooth={bluetooth} volume={volume}";
        }
    }

    public bool Fill(string kind, ObservableCollection<MenuRow> rows, IOutput output, Action<ObservableCollection<MenuRow>> deviceRows)
    {
        switch (kind)
        {
            case "battery":
                rows.Add(new HeaderRow { Label = "Battery", Detail = BatteryText() });
                if (_brightness is { } brightness && brightness.Supports(output))
                {
                    rows.Add(new DividerRow());
                    rows.Add(new SliderRow { Key = "brightness", Label = "Brightness", Value = BrightnessOf(output), Changed = value => SetBrightness(output, value) });
                }

                rows.Add(new DividerRow());
                deviceRows(rows);
                return true;
            case "wifi":
                FillWifi(rows);
                return true;
            case "bluetooth":
                FillBluetooth(rows);
                return true;
            case "sound":
                if (_volume is not { IsAvailable: true } volume)
                {
                    rows.Add(new HeaderRow { Label = "Sound" });
                    rows.Add(new NoteRow { Label = "No sound output" });
                    return true;
                }

                rows.Add(new HeaderRow { Label = "Sound", Detail = $"{Math.Round(volume.Volume * 100)}%" });
                rows.Add(new SliderRow { Key = "volume", Label = "Volume", Value = Math.Min(1, volume.Volume), Changed = value => _volume?.SetVolume((float)value) });
                rows.Add(new DividerRow());
                rows.Add(new ToggleRow { Key = "mute", Label = "Mute", On = volume.Muted, Changed = muted => _volume?.SetMuted(muted) });
                return true;
            default:
                return false;
        }
    }

    public void SyncSliders(string kind, IEnumerable<MenuRow> rows, IOutput output)
    {
        foreach (var row in rows)
        {
            switch (row)
            {
                case SliderRow { Key: "volume" } volumeRow when _volume is { IsAvailable: true } volume:
                    volumeRow.Sync(Math.Min(1, volume.Volume));
                    break;
                case SliderRow { Key: "brightness" } brightnessRow:
                    brightnessRow.Sync(BrightnessOf(output));
                    break;
                case ToggleRow { Key: "mute" } mute when _volume is { } sink:
                    mute.On = sink.Muted;
                    break;
                case HeaderRow header when kind == "battery":
                    header.Detail = BatteryText();
                    break;
                case HeaderRow header when kind == "sound" && _volume is { } sound:
                    header.Detail = $"{Math.Round(sound.Volume * 100)}%";
                    break;
            }
        }
    }

    public string BatteryText()
    {
        if (_battery is not { IsAvailable: true, IsPresent: true } battery)
        {
            return _battery is { IsAvailable: true } ? "No battery" : "Not available";
        }

        var percent = $"{Math.Round(battery.Percentage).ToString(CultureInfo.InvariantCulture)}%";
        return battery.State switch
        {
            BatteryState.Charging => $"Charging, {percent}",
            BatteryState.Full => "Charged",
            _ => percent,
        };
    }

    private double BrightnessOf(IOutput output)
    {
        if (_brightness is not { } brightness || !brightness.TryGet(output, out var value))
        {
            return 1;
        }

        var max = brightness.Max(output);
        return max == 0 ? 1 : value / (double)max;
    }

    private void SetBrightness(IOutput output, double value)
    {
        if (_brightness is { } brightness && brightness.Supports(output))
        {
            var max = brightness.Max(output);
            brightness.Set(output, (uint)Math.Clamp(Math.Round(value * max), 1, max));
        }
    }

    private void FillWifi(ObservableCollection<MenuRow> rows)
    {
        if (_network is not { IsAvailable: true, HasWirelessDevice: true } network)
        {
            rows.Add(new HeaderRow { Label = "Wi-Fi" });
            rows.Add(new NoteRow { Label = Unavailable(_network, "NetworkManager isn't running, so Wi-Fi can't be shown.", "No Wi-Fi device") });
            return;
        }

        rows.Add(new ToggleRow { Key = "wifi", Label = "Wi-Fi", On = network.WirelessEnabled, Changed = on => _ = network.SetWirelessEnabled(on) });
        if (network.WirelessEnabled)
        {
            rows.Add(new DividerRow());
            foreach (var point in network.AccessPoints)
            {
                var secured = point.Security is not AccessPointSecurity.Open;
                rows.Add(new ActionRow
                {
                    Key = $"ap:{point.Ssid}",
                    Label = point.Ssid,
                    Detail = $"{(secured ? "secured  " : "")}{point.Strength}%",
                    Checked = point.IsActive,
                    Enabled = JoinAllowed,
                    Invoked = JoinAllowed ? () => Join(point) : null,
                });
            }
        }

        if (_wifiAsking is { } asking)
        {
            rows.Add(new DividerRow());
            rows.Add(new PassphraseRow
            {
                Key = "passphrase",
                Label = $"Passphrase for {asking.Ssid}",
                Submitted = text => JoinSecured(network, asking, text),
                Cancelled = () =>
                {
                    _wifiAsking = null;
                    WifiMenusChanged?.Invoke();
                },
            });
        }

        rows.Add(new DividerRow());
        rows.Add(new NoteRow { Key = "settings", Label = _wifiMessage ?? "Enterprise networks need your network settings." });
    }

    private void JoinSecured(NetworkStatus network, NetworkAccessPoint point, string text)
    {
        _wifiAsking = null;
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(Math.Max(1, System.Text.Encoding.UTF8.GetMaxByteCount(text.Length)));
        Task<NetworkActivation> pending;
        try
        {
            var written = System.Text.Encoding.UTF8.GetBytes(text, buffer);
            pending = network.ActivateSecured(point, buffer.AsSpan(0, written));
        }
        finally
        {
            buffer.AsSpan().Clear();
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        _report.Line($"WIFI join {point.Ssid} secured");
        _wifiMessage = $"Joining {point.Ssid}…";
        WifiMenusChanged?.Invoke();
        _ = ReportJoin(point.Ssid, pending);
    }

    private async Task ReportJoin(string ssid, Task<NetworkActivation> pending)
    {
        var result = await pending.ConfigureAwait(true);
        _wifiMessage = result.Succeeded ? $"Joined {ssid}" : result.Error ?? $"Couldn't join {ssid}.";
        _report.Line(result.Succeeded ? $"WIFI {ssid} ok" : $"WIFI {ssid} failed {result.Error}");
        WifiMenusChanged?.Invoke();
    }

    public void Join(NetworkAccessPoint point)
    {
        if (_network is not { } network || point.IsActive || !JoinAllowed)
        {
            return;
        }

        if (point.Saved is not null)
        {
            _ = network.ActivateSaved(point);
            _report.Line($"WIFI join {point.Ssid} saved");
            return;
        }

        if (point.Security == AccessPointSecurity.Open)
        {
            _ = network.ActivateOpen(point);
            _report.Line($"WIFI join {point.Ssid} open");
            return;
        }

        if (point.Security is AccessPointSecurity.Wpa or AccessPointSecurity.Wpa3)
        {
            _wifiAsking = point;
            _wifiMessage = null;
            _report.Line($"WIFI ask {point.Ssid} {(point.Security == AccessPointSecurity.Wpa3 ? "sae" : "psk")}");
            WifiMenusChanged?.Invoke();
            return;
        }

        _report.Line($"WIFI join {point.Ssid} needs-settings");
    }

    private static string Unavailable(SystemStatusReader? reader, string serviceMissing, string noDevice) => reader switch
    {
        { IsAvailable: false, ServiceMissing: true } => serviceMissing,
        { IsAvailable: false } => "This status isn't available right now.",
        _ => noDevice,
    };

    private void FillBluetooth(ObservableCollection<MenuRow> rows)
    {
        if (_bluetooth is not { IsAvailable: true, HasAdapter: true } bluetooth)
        {
            rows.Add(new HeaderRow { Label = "Bluetooth" });
            rows.Add(new NoteRow { Label = Unavailable(_bluetooth, "The Bluetooth service isn't running.", "No Bluetooth adapter") });
            return;
        }

        rows.Add(new ToggleRow { Key = "bluetooth", Label = "Bluetooth", On = bluetooth.Powered, Changed = on => _ = bluetooth.SetPowered(on) });
        if (!bluetooth.Powered)
        {
            return;
        }

        rows.Add(new DividerRow());
        if (bluetooth.Devices.Count == 0)
        {
            rows.Add(new NoteRow { Label = "No paired devices" });
        }

        foreach (var device in bluetooth.Devices)
        {
            rows.Add(new ActionRow
            {
                Key = $"bt:{device.Address}",
                Label = device.Name,
                Detail = device.Connected ? "Connected" : "",
                Checked = device.Connected,
                Invoked = () =>
                {
                    _ = device.Connected ? bluetooth.Disconnect(device) : bluetooth.Connect(device);
                    _report.Line($"BLUETOOTH {(device.Connected ? "disconnect" : "connect")} {device.Address}");
                },
            });
        }
    }
}
