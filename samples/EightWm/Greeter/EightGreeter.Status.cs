using System.Globalization;
using Basin;
using Basin.Diagnostics;
using Basin.Session;
using Basin.SystemStatus;

namespace EightWm;

internal sealed partial class EightGreeter
{
    private SystemStatusBus? _statusBus;
    private BatteryStatus? _battery;
    private NetworkStatus? _networkStatus;
    private CompositorSynchronizationContext? _context;

    private static bool NoPower => Environment.GetEnvironmentVariable("BASIN_GREETER_NO_POWER") == "1";

    private void WireStatus()
    {
        _context = new CompositorSynchronizationContext(_host.Loop);
        _signIn.PowerRequested += action => Power(action);
        if (_config.Settings.Battery || _config.Settings.Network)
        {
            _statusBus = new SystemStatusBus(_host.Loop);
        }

        if (_config.Settings.Battery && _statusBus is not null)
        {
            _battery = BatteryStatus.TryCreate(_statusBus, out var reason);
            if (_battery is null)
            {
                _log.Info($"status: no battery status, {reason}");
            }
            else
            {
                _battery.Changed += ShowStatus;
            }
        }

        if (_config.Settings.Network && _statusBus is not null)
        {
            _networkStatus = NetworkStatus.TryCreate(_statusBus, out var reason);
            if (_networkStatus is null)
            {
                _log.Info($"status: no network status, {reason}");
            }
            else
            {
                _networkStatus.Changed += ShowStatus;
                var menu = _network;
                menu.CanJoin = _config.Settings.NetworkJoin;
                menu.WifiToggled += on =>
                {
                    BasinReport.Line($"WIFI {(on ? "on" : "off")}");
                    _ = _networkStatus.SetWirelessEnabled(on);
                };
                menu.PassphraseEntered += (ssid, text) => JoinSecured(ssid, text);
            }
        }

        ShowStatus();
        _ = ProbePowerAsync();
    }

    private void ShowStatus()
    {
        var batteryVisible = _battery is { IsAvailable: true, IsPresent: true };
        var batteryText = _battery is { IsPresent: true } battery
            ? Math.Round(battery.Percentage).ToString(CultureInfo.InvariantCulture) + "%"
            : "";
        var networkVisible = _networkStatus is { IsAvailable: true };
        var (glyph, name) = ("", "");
        if (_networkStatus is { IsAvailable: true } network)
        {
            (glyph, name) = network.PrimaryKind switch
            {
                NetworkConnectionKind.Wireless => ("GlyphWifi", network.AccessPoints.FirstOrDefault(point => point.IsActive)?.Ssid ?? ""),
                NetworkConnectionKind.Ethernet => ("GlyphEthernet", "Wired"),
                NetworkConnectionKind.None => ("GlyphNoNetwork", "Not connected"),
                _ => ("GlyphEthernet", network.PrimaryConnectionType ?? ""),
            };
        }

        foreach (var curtain in Curtains())
        {
            curtain.BatteryText = batteryVisible ? batteryText : "";
            curtain.NetworkGlyph = glyph;
            curtain.NetworkName = name;
        }

        ShowNetworkMenu();
        BasinReport.Line($"STATUS battery={(batteryVisible ? batteryText : "none")} network={(networkVisible ? $"{_networkStatus!.PrimaryKind.ToString().ToLowerInvariant()}:{name}" : "none")}");
    }

    private void ShowNetworkMenu()
    {
        var menu = _network;
        if (_networkStatus is not { IsAvailable: true } network)
        {
            menu.Open = false;
            menu.Points.Clear();
            return;
        }

        menu.HasWifi = network.HasWirelessDevice;
        menu.ShowWifi(network.WirelessEnabled);
        menu.Points.Clear();
        if (!network.WirelessEnabled)
        {
            return;
        }

        foreach (var point in network.AccessPoints)
        {
            var chosen = point;
            var detail = point.Security switch
            {
                AccessPointSecurity.Enterprise => "Not supported",
                _ when point.Saved is not null => "Saved",
                AccessPointSecurity.Open => $"{point.Strength}%",
                _ => $"{"Secured"} {point.Strength}%",
            };
            menu.Points.Add(new NetworkPointEntry(point.Ssid, detail, point.Security != AccessPointSecurity.Enterprise, point.IsActive, new RelayCommand(_ => Join(chosen.Ssid))));
        }
    }

    internal void Join(string ssid)
    {
        if (_networkStatus is not { } network || network.AccessPoints.FirstOrDefault(point => point.Ssid == ssid) is not { } point)
        {
            BasinReport.Line($"NETWORK join {ssid} failed no such network");
            return;
        }

        var menu = _network;
        if (point.IsActive)
        {
            return;
        }

        if (point.Saved is not null)
        {
            BasinReport.Line($"NETWORK join {ssid} saved");
            menu.Message = "Connecting...";
            _ = Report(ssid, network.ActivateSaved(point));
            return;
        }

        switch (point.Security)
        {
            case AccessPointSecurity.Open:
                BasinReport.Line($"NETWORK join {ssid} open");
                menu.Message = "Connecting...";
                _ = Report(ssid, network.ActivateOpen(point));
                break;
            case AccessPointSecurity.Wpa or AccessPointSecurity.Wpa3:
                BasinReport.Line($"NETWORK ask {ssid} {(point.Security == AccessPointSecurity.Wpa3 ? "sae" : "psk")}");
                menu.AskingFor = ssid;
                break;
            default:
                BasinReport.Line($"NETWORK join {ssid} unsupported");
                menu.Message = "Not supported";
                break;
        }
    }

    internal void JoinSecured(string ssid, string text)
    {
        var menu = _network;
        menu.AskingFor = "";
        if (_networkStatus is not { } network || network.AccessPoints.FirstOrDefault(point => point.Ssid == ssid) is not { } point)
        {
            return;
        }

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

        BasinReport.Line($"NETWORK join {ssid} secured");
        menu.Message = "Connecting...";
        _ = ReportActivation(ssid, pending);
    }

    private async Task Report(string ssid, Task<bool> pending)
    {
        var done = await pending.ConfigureAwait(true);
        _network.Message = done ? "Connected" : "Not supported";
        BasinReport.Line($"NETWORK {ssid} {(done ? "ok" : "failed")}");
    }

    private async Task ReportActivation(string ssid, Task<NetworkActivation> pending)
    {
        var result = await pending.ConfigureAwait(true);
        _network.Message = result.Succeeded ? "Connected" : result.Error ?? "";
        BasinReport.Line(result.Succeeded ? $"NETWORK {ssid} ok" : $"NETWORK {ssid} failed {result.Error}");
    }

    private async Task ProbePowerAsync()
    {
        if (!_config.Settings.Power)
        {
            _signIn.PowerAvailable = false;
            return;
        }

        if (NoPower)
        {
            ShowPower(true, true, true);
            return;
        }

        bool suspend, reboot, off;
        if (!LogindActions.Available)
        {
            _log.Info($"power: no power menu, logind is not running");
            _context?.Post(_ => _signIn.PowerAvailable = false, null);
            return;
        }
        else
        {
            suspend = await LogindActions.CanAsync(LogindAction.Suspend).ConfigureAwait(false);
            reboot = await LogindActions.CanAsync(LogindAction.Reboot).ConfigureAwait(false);
            off = await LogindActions.CanAsync(LogindAction.PowerOff).ConfigureAwait(false);
        }

        _context?.Post(_ => ShowPower(suspend, reboot, off), null);
    }

    private void ShowPower(bool suspend, bool reboot, bool off)
    {
        _signIn.PowerAvailable = suspend || reboot || off;
        BasinReport.Line($"POWER can suspend={Word(suspend)} restart={Word(reboot)} shutdown={Word(off)}");
    }

    private void Power(string action)
    {
        LogindAction? chosen = action switch
        {
            "suspend" => LogindAction.Suspend,
            "restart" => LogindAction.Reboot,
            "shutdown" => LogindAction.PowerOff,
            _ => null,
        };
        if (chosen is not { } logind)
        {
            return;
        }

        if (NoPower || !LogindActions.Available)
        {
            BasinReport.Line($"POWER {action} skipped");
            _log.Info($"power: {action} skipped, no logind or BASIN_GREETER_NO_POWER is set");
            return;
        }

        BasinReport.Line($"POWER {action} requested");
        _ = RunPowerAsync(logind, action);
    }

    private async Task RunPowerAsync(LogindAction action, string word)
    {
        var done = await LogindActions.RunAsync(action).ConfigureAwait(false);
        if (!done)
        {
            _context?.Post(_ => BasinReport.Line($"POWER {word} failed"), null);
        }
    }

    private static string Word(bool value) => value ? "yes" : "no";

    private void DisposeStatus()
    {
        _battery?.Dispose();
        _networkStatus?.Dispose();
        _statusBus?.Dispose();
    }
}
