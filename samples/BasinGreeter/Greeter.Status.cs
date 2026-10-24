using System.Globalization;
using Basin.Diagnostics;
using Basin.Session;
using Basin.SystemStatus;

namespace BasinGreeter;

internal sealed partial class Greeter
{
    private SystemStatusBus? _statusBus;
    private BatteryStatus? _battery;
    private NetworkStatus? _network;

    private static bool NoPower => Environment.GetEnvironmentVariable("BASIN_GREETER_NO_POWER") == "1";

    private void WireStatus()
    {
        _model.Bar.PowerRequested += action => Power(action);
        if (_config.Settings.Battery || _config.Settings.Network)
        {
            _statusBus = new SystemStatusBus(_host.Loop);
        }

        if (_config.Settings.Battery && _statusBus is not null)
        {
            _battery = BatteryStatus.TryCreate(_statusBus, out var reason);
            if (_battery is null)
            {
                _log.Info($"no battery status: {reason}");
            }
            else
            {
                _battery.Changed += ShowStatus;
            }
        }

        if (_config.Settings.Network && _statusBus is not null)
        {
            _network = NetworkStatus.TryCreate(_statusBus, out var reason);
            if (_network is null)
            {
                _log.Info($"no network status: {reason}");
            }
            else
            {
                _network.Changed += ShowStatus;
                var menu = _model.Bar.Network;
                menu.CanJoin = _config.Settings.NetworkJoin;
                menu.WifiToggled += on =>
                {
                    BasinReport.Line($"WIFI {(on ? "on" : "off")}");
                    _ = _network.SetWirelessEnabled(on);
                };
                menu.PassphraseEntered += (ssid, text) => JoinSecured(ssid, text);
            }
        }

        ShowStatus();
        _ = ProbePowerAsync();
    }

    private void ShowStatus()
    {
        var bar = _model.Bar;
        bar.BatteryVisible = _battery is { IsAvailable: true, IsPresent: true };
        bar.BatteryText = _battery is { IsPresent: true } battery
            ? Math.Round(battery.Percentage).ToString(CultureInfo.InvariantCulture) + "%"
            : "";

        bar.NetworkVisible = _network is { IsAvailable: true };
        if (_network is { IsAvailable: true } network)
        {
            (bar.NetworkGlyph, bar.NetworkText) = network.PrimaryKind switch
            {
                NetworkConnectionKind.Wireless => ("GlyphWifi", network.AccessPoints.FirstOrDefault(point => point.IsActive)?.Ssid ?? ""),
                NetworkConnectionKind.Ethernet => ("GlyphWired", GreeterText.Wired),
                NetworkConnectionKind.None => ("GlyphOffline", GreeterText.Offline),
                _ => ("GlyphWired", network.PrimaryConnectionType ?? ""),
            };
        }

        ShowNetworkMenu();
        BasinReport.Line($"STATUS battery={(bar.BatteryVisible ? bar.BatteryText : "none")} network={(bar.NetworkVisible ? $"{_network!.PrimaryKind.ToString().ToLowerInvariant()}:{bar.NetworkText}" : "none")}");
    }

    private void ShowNetworkMenu()
    {
        var menu = _model.Bar.Network;
        if (_network is not { IsAvailable: true } network)
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
                AccessPointSecurity.Enterprise => GreeterText.NotSupported,
                _ when point.Saved is not null => GreeterText.Saved,
                AccessPointSecurity.Open => $"{point.Strength}%",
                _ => $"{GreeterText.Secured} {point.Strength}%",
            };
            menu.Points.Add(new UI.NetworkPointEntry(point.Ssid, detail, point.Security != AccessPointSecurity.Enterprise, point.IsActive, new UI.RelayCommand(() => Join(chosen.Ssid))));
        }
    }

    internal void Join(string ssid)
    {
        if (_network is not { } network || network.AccessPoints.FirstOrDefault(point => point.Ssid == ssid) is not { } point)
        {
            BasinReport.Line($"NETWORK join {ssid} failed no such network");
            return;
        }

        var menu = _model.Bar.Network;
        if (point.IsActive)
        {
            return;
        }

        if (point.Saved is not null)
        {
            BasinReport.Line($"NETWORK join {ssid} saved");
            menu.Message = GreeterText.Connecting;
            _ = Report(ssid, network.ActivateSaved(point));
            return;
        }

        switch (point.Security)
        {
            case AccessPointSecurity.Open:
                BasinReport.Line($"NETWORK join {ssid} open");
                menu.Message = GreeterText.Connecting;
                _ = Report(ssid, network.ActivateOpen(point));
                break;
            case AccessPointSecurity.Wpa or AccessPointSecurity.Wpa3:
                BasinReport.Line($"NETWORK ask {ssid} {(point.Security == AccessPointSecurity.Wpa3 ? "sae" : "psk")}");
                menu.AskingFor = ssid;
                break;
            default:
                BasinReport.Line($"NETWORK join {ssid} unsupported");
                menu.Message = GreeterText.NotSupported;
                break;
        }
    }

    internal void JoinSecured(string ssid, string text)
    {
        var menu = _model.Bar.Network;
        menu.AskingFor = "";
        if (_network is not { } network || network.AccessPoints.FirstOrDefault(point => point.Ssid == ssid) is not { } point)
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
        menu.Message = GreeterText.Connecting;
        _ = ReportActivation(ssid, pending);
    }

    private async Task Report(string ssid, Task<bool> pending)
    {
        var done = await pending.ConfigureAwait(true);
        _model.Bar.Network.Message = done ? GreeterText.Connected : GreeterText.ConnectFailed;
        BasinReport.Line($"NETWORK {ssid} {(done ? "ok" : "failed")}");
    }

    private async Task ReportActivation(string ssid, Task<NetworkActivation> pending)
    {
        var result = await pending.ConfigureAwait(true);
        _model.Bar.Network.Message = result.Succeeded ? GreeterText.Connected : result.Error ?? "";
        BasinReport.Line(result.Succeeded ? $"NETWORK {ssid} ok" : $"NETWORK {ssid} failed {result.Error}");
    }

    private async Task ProbePowerAsync()
    {
        if (!_config.Settings.Power)
        {
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
            _log.Info($"no power menu: logind is not running");
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
        var bar = _model.Bar;
        bar.CanSuspend = suspend;
        bar.CanRestart = reboot;
        bar.CanShutDown = off;
        bar.PowerVisible = suspend || reboot || off;
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
            _log.Info($"{action} skipped: no logind, or BASIN_GREETER_NO_POWER is set");
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
        _network?.Dispose();
        _statusBus?.Dispose();
    }
}
