using Basin;
using Basin.Capabilities;

namespace TinyComp;

internal sealed partial class TinyComp
{
    internal void ToggleX11Density(Basin.XWayland.XWaylandWindow window)
    {
        if (_densityControl is null || window.Surface is not { } surface || !_toplevels.TryFind(surface, out var info))
        {
            return;
        }

        var target = _xwaylandModule.DisplayOf(window)?.Density == X11Density.Low ? X11Density.Native : X11Density.Low;
        if (!_densityControl.TrySet(info.Id, target, ReportDensity, out var error))
        {
            _log.Warn($"cannot change the X server of {window.Class}: {error}");
            _report.Line($"X11DENSITY refused {error}");
        }
    }

    private void ReportDensity(X11DensityResult result) =>
        _report.Line($"X11DENSITY {result.Outcome} {result.Rule}{(result.LaunchId == 0 ? string.Empty : $" launch_id={result.LaunchId}")}{(result.Reason is { } reason ? $": {reason}" : string.Empty)}");
}
