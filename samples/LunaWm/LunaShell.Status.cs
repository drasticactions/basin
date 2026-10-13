using Basin.Capabilities;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private LunaStatus? _status;

    private void StartStatus()
    {
        _status = new LunaStatus(_host.Loop, _report, _log, "luna-wm");
        _status.Changed += OnStatusChanged;
        _status.WifiMenusChanged += RefreshWifiMenus;
        _status.Start(_services.Find<IOutputBrightness>());
    }

    private void StopStatus()
    {
        _status?.Dispose();
        _status = null;
    }

    private void OnStatusChanged()
    {
        foreach (var view in _views)
        {
            SyncIndicators(view);
            if (view.Menu is { Open: true } menu && menu.Kind != "app")
            {
                FillSystemMenu(view, menu, rebuild: false);
            }
        }
    }

    private void SyncIndicators(LunaView view)
    {
        if (view.Bar is { } bar)
        {
            _status?.Sync(bar.Model);
        }
    }

    internal string IndicatorState => _status?.IndicatorState ?? "battery=absent wifi=hidden bluetooth=hidden volume=hidden";
}
