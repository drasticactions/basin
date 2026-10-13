using Basin;
using Basin.Capabilities;
using Basin.XWayland;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private XWaylandWindow? DensityTarget(AppGroup app)
    {
        if (_densityControl is null || _xwaylandModule?.LowDensity is null || app.IsFinder)
        {
            return null;
        }

        return ActiveOf(app)?.X11 ?? app.Windows.FirstOrDefault(w => w.X11 is not null)?.X11;
    }

    private void ToggleX11Density(XWaylandWindow window)
    {
        if (_densityControl is null || window.Surface is not { } surface || !_services.Require<IToplevelModel>().TryFind(surface, out var info))
        {
            return;
        }

        var target = _xwaylandModule?.DisplayOf(window)?.Density == X11Density.Low ? X11Density.Native : X11Density.Low;
        if (!_densityControl.TrySet(info.Id, target, ReportDensity, out var error))
        {
            _log.Warn($"cannot change the X server of {window.Class}: {error}");
            _report.Line($"X11DENSITY refused {error}");
        }
    }

    private void ReportDensity(X11DensityResult result)
    {
        _report.Line($"X11DENSITY {result.Outcome} {result.Rule}{(result.LaunchId == 0 ? string.Empty : $" launch_id={result.LaunchId}")}{(result.Reason is { } reason ? $": {reason}" : string.Empty)}");
        RefreshApplicationMenu();
    }
}
